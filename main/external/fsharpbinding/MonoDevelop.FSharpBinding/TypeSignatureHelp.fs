﻿namespace MonoDevelop.FSharp

open System
open System.Collections.Generic
open System.Collections.Concurrent
open ExtCore.Control
open Mono.TextEditor
open MonoDevelop
open MonoDevelop.Components
open MonoDevelop.Core
open MonoDevelop.Ide.Editor
open MonoDevelop.Ide.Editor.Extension
open Microsoft.FSharp.Compiler.SourceCodeServices
open Microsoft.FSharp.Compiler
open System.Reactive.Linq

type SignatureHelpMarker(document, text, font, lineNr, line) =
    inherit TextLineMarker()
    let mutable text' = text
    //let mutable layout: Pango.Layout option = None
    let tag = obj()
    static member FontScale = 0.8
    member x.Text with get() = text' and set(value) = text' <- value
    //override this.Draw(editor, g, _metrics) = ()
    interface ITextLineMarker with
        member x.Line with get() = line
        member x.IsVisible with get() = true and set(value) = ()
        member x.Tag with get() = tag and set(value) = ()

    interface IExtendingTextLineMarker with
        member x.IsSpaceAbove with get() = true
        member x.GetLineHeight editor = editor.LineHeight * (1.0 + SignatureHelpMarker.FontScale)
        member x.Draw(editor, g, lineNr, _lineArea) =
            g.SetSourceRGB(0.5, 0.5, 0.5)
            let line = editor.GetLine lineNr

            use layout = new Pango.Layout(editor.PangoContext, FontDescription=font)
            layout.SetText text'
            let x = editor.ColumnToX (line, (line.GetIndentation(document).Length+1)) - editor.HAdjustment.Value + editor.TextViewMargin.XOffset + (editor.TextViewMargin.TextStartPosition |> float)
            let y = (editor.LineToY lineNr) - editor.VAdjustment.Value

            let currentPoint = g.CurrentPoint
            g.MoveTo(x, y + editor.LineHeight * (1.0 - SignatureHelpMarker.FontScale))
            //g.MoveTo(x, y + 5.0)
            g.ShowLayout layout
            g.MoveTo currentPoint

module signatureHelp =
    let getOffset (editor:TextEditor) (pos:Range.pos) =
        editor.LocationToOffset (pos.Line, pos.Column+1)

    let getUnusedOpens (markers:ConcurrentDictionary<int, SignatureHelpMarker>) (context:DocumentContext) (editor:TextEditor) (data:TextEditorData) font =
        let document = data.Document
        let ast =
            maybe {
                let! ast = context.TryGetAst()
                let! pd = context.TryGetFSharpParsedDocument()

                return ast, pd
            }
        let extractSignature (FSharpToolTipText tips) =
            let getSignature (str: string) =
                let nlpos = str.IndexOfAny([|'\r';'\n'|])
                let firstLine =
                    if nlpos > 0 then str.[0..nlpos-1]
                    else str
                let res = 
                    if firstLine.StartsWith("type ", StringComparison.Ordinal) then
                        let index = firstLine.LastIndexOf("=", StringComparison.Ordinal)
                        if index > 0 then firstLine.[0..index-1]
                        else firstLine
                    else firstLine
                let index = res.IndexOf ": "
                res.[index+2..]

            let firstResult x =
                match x with
                | FSharpToolTipElement.Single (t, _) when not (String.IsNullOrWhiteSpace t) -> Some t
                | FSharpToolTipElement.Group gs -> List.tryPick (fun (t, _) -> if not (String.IsNullOrWhiteSpace t) then Some t else None) gs
                | _ -> None

            tips
            |> Seq.sortBy (function FSharpToolTipElement.Single _ -> 0 | _ -> 1)
            |> Seq.tryPick firstResult
            |> Option.map getSignature
            |> Option.fill ""

        ast |> Option.iter (fun (ast, pd) ->
            let symbols = pd.AllSymbolsKeyed.Values |> List.ofSeq
            let topVisibleLine = ((data.VAdjustment.Value / data.LineHeight) |> int) + 1
            let bottomVisibleLine =
                Math.Min(data.LineCount - 1,
                    topVisibleLine + ((data.VAdjustment.PageSize / data.LineHeight) |> int))

            let funs = 
                symbols

                |> List.filter(fun s -> s.IsFromDefinition)
                |> List.filter(fun s -> match s with 
                                        | SymbolUse.MemberFunctionOrValue mfv -> mfv.FullType.IsFunctionType
                                        | _ -> false)
                |> List.map(fun f -> f.RangeAlternate.StartLine, f)
                |> Map.ofList
            

            // remove any markers that are in the wrong positions
            //markers.Keys
            //|> Seq.filter(fun lineNumber -> lineNumber >= topVisibleLine && lineNumber <= bottomVisibleLine)
            //|> Seq.iter(fun lineNr -> 
            //                if not (funs.ContainsKey lineNr) then
            //                    markers.TryRemove(lineNr) |> ignore)
            let lineNumbersWithMarkersToRemove = 
                [topVisibleLine..bottomVisibleLine] |> List.filter(not << funs.ContainsKey)
            if not lineNumbersWithMarkersToRemove.IsEmpty then
                for lineNr in lineNumbersWithMarkersToRemove do
                    let line = editor.GetLine lineNr
                    editor.GetLineMarkers line 
                    |> Seq.iter(fun m -> Runtime.RunInMainThread(fun() -> editor.RemoveMarker m) |> ignore)
                document.CommitUpdateAll()
            //editor.GetLineMarkers(

            let addMarker text lineNr line =
                let newMarker = SignatureHelpMarker(document, text, font, lineNr, line)
                markers.TryAdd (lineNr, newMarker) |> ignore
                document.AddMarker(lineNr, newMarker)
                newMarker :> ITextLineMarker
                //let line = editor.GetLine lineNr

                //let offset = line.Offset
                //let location = DocumentLocation(lineNr, 0)
                //// use a smart marker to store the signature marker
                //// so that we don't need to keep track of markers separately
                //let currentSmartTag = TextMarkerFactory.CreateSmartTagMarker (editor, offset, location)
                ////currentSmartTag.CancelPopup += CurrentSmartTag_CancelPopup;
                ////currentSmartTag.ShowPopup += CurrentSmartTag_ShowPopup;
                //currentSmartTag.Tag <- newMarker
                //currentSmartTag.IsVisible <- false
                //editor.AddMarker (currentSmartTag)

            funs |> Map.iter(fun _l f ->
                let range = f.RangeAlternate
                let lineText = editor.GetLineText(range.StartLine)

                let line = editor.GetLine range.StartLine
                //editor.sm
                //let res, marker = markers.TryGetValue range.StartLine
                let marker = editor.GetLineMarkers line |> Seq.tryHead

                //if marker.IsNone then
                    //add placeholder
                let marker = marker |> Option.getOrElse(fun() -> addMarker "" range.StartLine line)
                if range.StartLine >= topVisibleLine && range.EndLine <= bottomVisibleLine then
                    async {
                        let! tooltip = ast.GetToolTip(range.StartLine, range.StartColumn, lineText)
                        tooltip |> Option.iter(fun (tooltip, lineNr) ->
                            let text = extractSignature tooltip
                            LoggingService.logDebug "Line %d - %s" lineNr text
                            //let res, marker = markers.TryGetValue range.StartLine
                            //let line = editor.GetLine lineNr
                            //|> Option.iter(fun marker ->
                            match marker with
                            | :? SignatureHelpMarker as sigMarker ->
                                if sigMarker.Text <> text then
                                     sigMarker.Text <- text
                                     document.CommitLineUpdate lineNr
                            | _ -> ())
                            //else
                                //addMarker text range.StartLine )
                    } |> Async.StartImmediate)
                )

type SignatureHelp() =
    inherit TextEditorExtension()
    let mutable disposable = None : IDisposable option

    let throttle (due:TimeSpan) observable =
        Observable.Throttle(observable, due)

    override x.Initialize() =
        let data = x.Editor.GetContent<ITextEditorDataProvider>().GetTextEditorData()

        let markers = ConcurrentDictionary<int, SignatureHelpMarker>()
        let editorFont = data.Options.Font

        let font = new Pango.FontDescription(AbsoluteSize=float(editorFont.Size) * SignatureHelpMarker.FontScale, Family=editorFont.Family)

        disposable <-
            Some
                (Observable.merge x.Editor.VAdjustmentChanged x.DocumentContext.DocumentParsed
                |> throttle (TimeSpan.FromMilliseconds 100.)
                |> Observable.subscribe (fun _ -> signatureHelp.getUnusedOpens markers x.DocumentContext x.Editor data font))

    override x.Dispose() = disposable |> Option.iter (fun en -> en.Dispose ())