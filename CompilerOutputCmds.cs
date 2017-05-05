using System;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using System.Linq;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Process = System.Diagnostics.Process;

namespace DevUtils
{
	/// <summary>
	/// Command Group dealing with compiler generated outputs
	/// </summary>
	internal sealed class CompilerOutputCmds
	{
		public const int cmdShowAssembly         = 0x100;
		public const int cmdShowPreprocessed     = 0x101;
		public const int cmdShowDecompiledCSharp = 0x102; // C#

		/// <summary>
		/// Command menu group (command set Guid).
		/// </summary>
		public static readonly Guid guidDevUtilsCmdSet = new Guid("57945603-4aa5-4f1b-85c4-b3c450332e5e");

		/// <summary>
		/// VS Package that provides this command, not null.
		/// </summary>
		private readonly DevUtilsPackage package;

		public static CompilerOutputCmds Instance { get; private set; }

		private IServiceProvider serviceProvider => package;

		// get the DTE object for this package
		private DTE2 dte => package.dte;

		private CompilerOutputCmds(DevUtilsPackage package)
		{
			this.package = package;

			// Add our command handlers for menu (commands must exist in the .vsct file)
			OleMenuCommandService mcs = serviceProvider.GetService(typeof (IMenuCommandService)) as OleMenuCommandService;
			if (null != mcs)
			{
				// Create the command for the menu item.
				CommandID menuCommandID = new CommandID(guidDevUtilsCmdSet, cmdShowAssembly);
				var cmd = new OleMenuCommand((s, e) => showCppOutput(1), changeHandler, beforeQueryStatus, menuCommandID);
				cmd.Properties["lang"] = "C/C++";
				mcs.AddCommand(cmd);

				menuCommandID = new CommandID(guidDevUtilsCmdSet, cmdShowPreprocessed);
				cmd = new OleMenuCommand((s, e) => showCppOutput(2), changeHandler, beforeQueryStatus, menuCommandID);
				cmd.Properties["lang"] = "C/C++";
				mcs.AddCommand(cmd);

				menuCommandID = new CommandID(guidDevUtilsCmdSet, cmdShowDecompiledCSharp);
				cmd = new OleMenuCommand((s, e) => showDecompiledCSharp(), changeHandler, beforeQueryStatus, menuCommandID);
				cmd.Properties["lang"] = "CSharp";
				mcs.AddCommand(cmd);
			}
		}

		public static void Initialize(DevUtilsPackage package)
		{
			Instance = new CompilerOutputCmds(package);
		}

		private void changeHandler(object sender, EventArgs e)
		{
		}

		/// called when the context menu is opened
		///
		/// makes entries only visible if the document is a supported language
		/// but enable it only if it's part of a project
		private void beforeQueryStatus(object sender, EventArgs e)
		{
			OleMenuCommand menuCommand = sender as OleMenuCommand;
			if (menuCommand == null)
				return;

			// reset
			menuCommand.Enabled = true;
			menuCommand.Visible = true;

			// cases:
			// normal .cpp in project
			// external .cpp file => projectItem != null, projectItem.Object == null
			// stdlib header => projectItem == null
			// "Solution Files"   => projectItem.Object == null
			// other file types like .txt or .cs

			Document doc = dte.ActiveDocument;

			// first check if it's part of a project
			ProjectItem projectItem = doc.ProjectItem;
			if (projectItem?.Object == null || projectItem.ContainingProject?.Object == null)
			{
				menuCommand.Enabled = false;
				package.writeStatus("This file is not recognized as part of any project");
			}

			if (currentFileIsHeaderFile())
				menuCommand.Enabled = true;

			if (doc.Language != (string) menuCommand.Properties["lang"])
				menuCommand.Visible = false;
		}

		private bool currentFileIsHeaderFile()
		{
			string fileName = dte.ActiveDocument.Name;
			return fileName.EndsWith(".h", StringComparison.OrdinalIgnoreCase) ||
				fileName.EndsWith(".hpp", StringComparison.OrdinalIgnoreCase) ||
				!fileName.Contains("."); // stdlib headers
		}

		// if the current document is an .h file, at least try switching to a potential cpp file of the same name
		private bool trySwitchToCppFile()
		{
			string filePath = dte.ActiveDocument.FullName;
			string altPath = Path.ChangeExtension(filePath, "cpp");
			if (!File.Exists(altPath))
			{
				altPath = Path.ChangeExtension(filePath, "cc");
				if (!File.Exists(altPath))
				{
					return false;
				}
			}
			dte.Documents.Open(altPath);
			return true;
		}

		// helpers for header files
		private static string _lastFunctionOfInterest;
		private static string _lastCodeLine;

		private void inspectCurrentCppFile(out string functionOfInterest, out string curCodeLine)
		{
			// get the currently active document from the IDE
			Document doc = dte.ActiveDocument;
			TextDocument tdoc = doc.Object("TextDocument") as TextDocument;

			if (tdoc == null)
				throw new Exception("Could not obtain the active TextDocument object");

			// do we want a line found in a previous run?
			if (_lastCodeLine != null)
			{
				functionOfInterest = _lastFunctionOfInterest;
				_lastFunctionOfInterest = null;
				curCodeLine = _lastCodeLine;
				_lastCodeLine = null;

				// don't exit if we are still in some header file
				if (!currentFileIsHeaderFile())
					return;
			}

			// find suitable code line near the cursor
			// TODO: comments are removed when preprocessing and thus can't find a line with comments
			TextSelection selection = tdoc.Selection;
			int line = selection.TopPoint.Line;
			EditPoint editPoint = tdoc.CreateEditPoint();
			curCodeLine = editPoint.GetLines(line, line + 1);

			if (string.IsNullOrWhiteSpace(curCodeLine))
			{
				++line;
				curCodeLine = editPoint.GetLines(line, line + 1);
				if (string.IsNullOrWhiteSpace(curCodeLine))
				{
					line -= 2;
					curCodeLine = editPoint.GetLines(line, line + 1);
					if (string.IsNullOrWhiteSpace(curCodeLine))
						throw new Exception("Choose a distinctive line of code inside a function or the function definition itself.");
				}
				selection.GotoLine(line, true);
			}

			// get currently viewed function
			functionOfInterest = "";
			CodeElement codeEl = selection.TopPoint.CodeElement[vsCMElement.vsCMElementFunction];
			if (codeEl != null)
				functionOfInterest = codeEl.FullName;
			else
			{
				dte.StatusBar.Text = "Warning: could not get function object from the IDE.";
				dte.StatusBar.Highlight(true);
			}

			// TODO: in case of a template this gets something like funcName<T>, the assembly contains funcName<actualType>
			//       it doesn't in the case of macros either, e.g. gets _tmain but in the asm it will be wmain

			// now that we extracted the function of interest handle the header file case
			if (currentFileIsHeaderFile() && !trySwitchToCppFile())
			{
				_lastFunctionOfInterest = functionOfInterest;
				_lastCodeLine = curCodeLine;

				throw new Exception("Please open a cpp file calling this code and re-run.");
			}
		}

		// 1: show assembly code for currently open source file
		// 2: show preprocessed source code
		private void showCppOutput(int mode = 1)
		{
			string functionOfInterest;
			string curCodeLine;
			inspectCurrentCppFile(out functionOfInterest, out curCodeLine);

			// get current configuration
			Document doc = dte.ActiveDocument;
			Project proj = doc.ProjectItem.ContainingProject;
			ConfigurationManager mgr = proj.ConfigurationManager;
			string platform = mgr.ActiveConfiguration.PlatformName;
			string conf = mgr.ActiveConfiguration.ConfigurationName;

			// find the currently active configuration for the current file
			// don't use SolutionBuild as there may be mixed platforms
			// use late binding for version independence
			dynamic file = doc.ProjectItem.Object;                        // as VCFile
			dynamic fileconfigs = file.FileConfigurations;                // as IVCCollection
			dynamic fileconfig = fileconfigs.Item(conf + "|" + platform); // as VCFileConfiguration
			dynamic tool = fileconfig.Tool;                               // VCCLCompilerTool

			// brute-force way to prevent project file modifications
			// copy to TEMP and restore later
			proj.Save();
			string tempCopyPath = Path.Combine(Path.GetTempPath(), proj.Name);
			File.Copy(proj.FullName, tempCopyPath, true);

			string generatedFile;
			if (mode == 1)
			{
				// asmListingAsmSrc => '.asm'
				generatedFile = Path.GetTempFileName() + ".asm";

				tool.WholeProgramOptimization = false;
				tool.AssemblerOutput = Enum.Parse(tool.AssemblerOutput.GetType(), "asmListingAsmSrc");
				tool.AssemblerListingLocation = generatedFile;
				tool.WarnAsError = false;
			}
			else /*if (mode == 2)*/
			{
				generatedFile = Path.GetTempFileName() + ".cpp";

				tool.GeneratePreprocessedFile = Enum.Parse(tool.GeneratePreprocessedFile.GetType(), "preprocessYes");
				// there's no separate option for this, so misuse /Fo
				tool.ObjectFile = generatedFile;
			}

			// VCFileConfiguration.Compile often does not work anymore and blocks the GUI
			// so just use the Compile command by installing a one-time callback
			_dispBuildEvents_OnBuildProjConfigDoneEventHandler onProjBuildDone = null;
			onProjBuildDone = (project, projectConfig, platfrm, solutionConfig, success) =>
			{
				// Unique name is project name including solution folders hierarchy
				if (project != proj.UniqueName)
					return;

				dte.Events.BuildEvents.OnBuildProjConfigDone -= onProjBuildDone;

				if (File.Exists(tempCopyPath)) // just to be safe
				{
					File.Delete(proj.FullName);
					File.Move(tempCopyPath, proj.FullName);
				}

				if (success)
					postCompileCpp(generatedFile, mode, functionOfInterest, curCodeLine);
			};
			dte.Events.BuildEvents.OnBuildProjConfigDone += onProjBuildDone;
			dte.ExecuteCommand("Build.Compile");
		}

		private void postCompileCpp(string generatedFile, int mode, string functionOfInterest, string curCodeLine)
		{
			if (!File.Exists(generatedFile))
			{
				package.showMsgBox("Could not find expected output file\n" + generatedFile);
				return;
			}

			// clean the preprocessed output
			// TODO: do this in a better way
			if (mode == 2)
			{
				var input = new StreamReader(generatedFile);
				generatedFile = Path.GetTempFileName() + ".cpp";
				var output = new StreamWriter(generatedFile);

				while (input.Peek() >= 0)
				{
					string curReadLine = input.ReadLine();
					if (curReadLine != "")
						output.WriteLine(curReadLine);
				}
				input.Close();
				output.Close();
			}

			// TODO: there are a thousand ways to open a file
			//			dte.Documents.Open(asmFile, EnvDTE.Constants.vsViewKindCode);
			//			dte.ExecuteCommand("File.OpenFile", asmFile);
			Window tmp = dte.ItemOperations.OpenFile(generatedFile, Constants.vsViewKindCode);
			TextDocument genFileWindow = (TextDocument)tmp.Document.Object("TextDocument");

			// crashes VS
			//			bool ddd = genFileWindow.ReplacePattern("^$\n", "", (int)vsFindOptions.vsFindOptionsRegularExpression);
			// http://stackoverflow.com/questions/12453160/remove-empty-lines-in-text-using-visual-studio
			// ^:b*$\n -> ^(?([^\r\n])\s)*\r?$\r?\n

			// now try to find the function the user was looking at

			// if it's a template the fullName will be like ns::bar<T>
			// try to find an instantiation instead then
			int bracketPos = functionOfInterest.IndexOf("<", StringComparison.Ordinal);
			if (bracketPos > 0)
				functionOfInterest = functionOfInterest.Substring(0, bracketPos + 1);

			TextSelection textSelObj = genFileWindow.Selection;
			// first try to find the function
			// TODO: for some reason vsFindOptions.vsFindOptionsFromStart option doesn't work
			textSelObj.StartOfDocument();
			bool res = textSelObj.FindText("PROC ; " + functionOfInterest, (int)vsFindOptions.vsFindOptionsMatchCase);
			if (!res && mode == 1)
			{
				dte.StatusBar.Text = "Couldn't find function '" + functionOfInterest + "'";
				dte.StatusBar.Highlight(true);
			}

			// then search for the code line
			// it might not be there if it's optimized away
			if (!string.IsNullOrWhiteSpace(curCodeLine))
				textSelObj.FindText(curCodeLine.Trim(), (int)vsFindOptions.vsFindOptionsMatchCase);

			textSelObj.StartOfLine();
		}

		// callback: show decompiled C# code
		private void showDecompiledCSharp()
		{

			Document doc = dte.ActiveDocument;
			var tdoc = doc.Object("TextDocument") as TextDocument;
			if (tdoc == null)
			{
				package.showMsgBox("Could not obtain the active TextDocument object");
				return;
			}

			// get currently viewed code element
			// try different levels as fallback
			var selection = tdoc.Selection;
			var actPt = selection.ActivePoint;
			CodeElement codeEl = actPt.CodeElement[vsCMElement.vsCMElementProperty] ??
			                     actPt.CodeElement[vsCMElement.vsCMElementFunction] ??
			                     actPt.CodeElement[vsCMElement.vsCMElementClass];

			if (codeEl == null)
			{
				package.showMsgBox("Could not find any supported code elements around the cursor.");
				return;
			}

			dte.ExecuteCommand("Build.BuildSelection");

			string functionOfInterest = null;
			switch (codeEl.Kind)
			{
			case vsCMElement.vsCMElementProperty:
				functionOfInterest = "P:" + codeEl.FullName;
				break;
			case vsCMElement.vsCMElementFunction:
				var func = codeEl as CodeFunction2;
				functionOfInterest = "M:" + func.FullName;

				if (func.IsGeneric)
				{
					// just fall back
					codeEl = func.Parent as CodeElement;
					goto case vsCMElement.vsCMElementClass;
				}

				if ((func.FunctionKind & vsCMFunction.vsCMFunctionConstructor) == vsCMFunction.vsCMFunctionConstructor)
				{
					int idx = functionOfInterest.LastIndexOf('.');
					var basis = functionOfInterest.Remove(idx + 1) + '#';
					if (func.IsShared)
						basis += 'c';
					functionOfInterest = basis + "ctor";
				}
				else if ((func.FunctionKind & vsCMFunction.vsCMFunctionDestructor) == vsCMFunction.vsCMFunctionDestructor)
				{
					int idx = functionOfInterest.LastIndexOf('.');
					var basis = functionOfInterest.Remove(idx + 1);
					functionOfInterest = basis + "Finalize";
				}
				break;
			case vsCMElement.vsCMElementClass:
				// var cl = codeEl as CodeClass2;
				functionOfInterest = "T:" + codeEl.FullName;
				break;
			}

			int bidx = functionOfInterest.IndexOf('<');
			if (bidx > 0)
			{
				int cnt = functionOfInterest.Count(c => c == ',') + 1;
				functionOfInterest = functionOfInterest.Substring(0, bidx) + '`' + cnt + functionOfInterest.Substring(functionOfInterest.IndexOf('>', bidx + 1) + 1);
			}

			string args = @"/language:C# /clearList /navigateTo:";
			args += functionOfInterest;

			// already checked beforehand that the file is part of a project
			Configuration config = doc.ProjectItem.ContainingProject.ConfigurationManager.ActiveConfiguration;
			// misuse this property to get a path to the output assembly
			Property prop = config.Properties.Item("CodeAnalysisInputAssembly");
			string assemblyPath = prop.Value.ToString();
			assemblyPath = Path.Combine(Path.GetDirectoryName(doc.ProjectItem.ContainingProject.FullName), assemblyPath);

			if (!File.Exists(assemblyPath))
			{
				package.showMsgBox($"Output file {assemblyPath} does not exist!");
				return;
			}

			args += " \"" + assemblyPath + '"';
			var proc = new Process
			{
				StartInfo = new ProcessStartInfo(@"ILSpy.exe", args)
			};
			try
			{
				proc.Start();
			}
			catch (Win32Exception)
			{
				package.showMsgBox("Could not execute ILSpy. Is it on the path?", "ILSpy missing");
			}
		}

	}
}
