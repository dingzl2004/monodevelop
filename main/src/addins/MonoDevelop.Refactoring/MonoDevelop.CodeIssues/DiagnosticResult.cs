// 
// CodeAnalysisRunner.cs
//  
// Author:
//       Mike Krüger <mkrueger@xamarin.com>
// 
// Copyright (c) 2012 Xamarin Inc. (http://xamarin.com)
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//#define PROFILE
using System;
using MonoDevelop.AnalysisCore;
using ICSharpCode.NRefactory.Refactoring;

namespace MonoDevelop.CodeIssues
{
	class DiagnosticResult : Result
	{
		readonly Microsoft.CodeAnalysis.Diagnostic diagnostic;

		public Microsoft.CodeAnalysis.Diagnostic Diagnostic {
			get {
				return diagnostic;
			}
		}

		public DiagnosticResult (Microsoft.CodeAnalysis.Diagnostic diagnostic) : base (diagnostic.Location.SourceSpan, diagnostic.GetMessage ())
		{
			if (diagnostic == null)
				throw new ArgumentNullException ("diagnostic");
			this.diagnostic = diagnostic;
			var marker = IssueMarker.WavedLine;
//			var nrefactoryDiagnostic = diagnostic as NRefactoryDiagnostic;
			
			SetSeverity (ConvertSeverity (diagnostic.Severity), marker); 
		}

//		static IssueMarker ConvertIssueMarker (string kind)
//		{
//			switch (kind) {
//			case ICSharpCode.NRefactory6.CSharp.Refactoring.IssueKinds.WavedLine:
//				return IssueMarker.WavedLine;
//			case ICSharpCode.NRefactory6.CSharp.Refactoring.IssueKinds.DottedLine:
//				return IssueMarker.DottedLine;
//			case ICSharpCode.NRefactory6.CSharp.Refactoring.IssueKinds.GrayOut:
//				return IssueMarker.GrayOut;
//			}
//			return IssueMarker.WavedLine;
//		}

		static Severity ConvertSeverity (Microsoft.CodeAnalysis.DiagnosticSeverity severity)
		{
			switch (severity) {
			case Microsoft.CodeAnalysis.DiagnosticSeverity.None:
				return Severity.None;
			case Microsoft.CodeAnalysis.DiagnosticSeverity.Info:
				return Severity.Hint;
			case Microsoft.CodeAnalysis.DiagnosticSeverity.Warning:
				return Severity.Warning;
			case Microsoft.CodeAnalysis.DiagnosticSeverity.Error:
				return Severity.Error;
			default:
				throw new ArgumentOutOfRangeException ("severity", severity, "not supported");
			}
		}
	}
}

