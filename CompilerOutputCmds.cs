using System;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Process = System.Diagnostics.Process;

namespace VSPackage.DevUtils
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
		private EnvDTE80.DTE2 dte => package.dte;

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

			// cases:
			// normal .cpp in project
			// external .cpp file => projectItem != null, projectItem.Object == null
			// "Solution Files"   => projectItem.Object == null
			// other file types like .txt or .cs

			Document doc = dte.ActiveDocument;

			// first check if it's part of a project
			ProjectItem projectItem = doc.ProjectItem;
			if (projectItem?.Object == null || projectItem.ContainingProject?.Object == null)
				menuCommand.Enabled = false;

			if (doc.Language != (string) menuCommand.Properties["lang"])
				menuCommand.Visible = false;
		}

		// 1: show assembly code for currently open source file
		// 2: show preprocessed source code
		private void showCppOutput(int mode = 1)
		{
			// if in a header try to open the cpp file
			switchToCppFile();

			// get the currently active document from the IDE
			Document doc = dte.ActiveDocument;
			TextDocument tdoc = doc.Object("TextDocument") as TextDocument;

			if (tdoc == null)
			{
				package.showMsgBox("Could not obtain the active TextDocument object");
				return;
			}

			// get currently viewed function
			string functionOfInterest = "";
			TextSelection selection = doc.Selection as TextSelection;
			CodeElement codeEl = selection.ActivePoint.CodeElement[vsCMElement.vsCMElementFunction];

			if (codeEl == null)
			{
				dte.StatusBar.Text = "You should place the cursor inside a function.";
				dte.StatusBar.Highlight(true);
			}
			else
				functionOfInterest = codeEl.FullName;
			// TODO: in case of a template this gets something like funcName<T>, the assembly contains funcName<actualType>
			//       it doesn't in the case of macros either, e.g. gets _tmain but in the asm it will be wmain

			// http://www.viva64.com/en/a/0082/#ID0ELOBK
			// EditPoint directly manipulates text buffer data instead of operating with the text through the editor UI.
			int line = selection.ActivePoint.Line;
			EditPoint editPoint = tdoc.CreateEditPoint();
			string curCodeLine = editPoint.GetLines(line, line + 1);
			// TODO: comments are removed when preprocessing and thus can't find a line with comments

			// get current configuration
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


			// save original settings to restore them later

			// there seems to be no better way
			// DTE undo contexts doesn't cover these project settings
			// unload/reload the project may close all open files
			// manipulating proj.Saved or .IsDirty does not work
			bool lto                 = tool.WholeProgramOptimization;
			dynamic asmtype          = tool.AssemblerOutput;
			dynamic genPreprocessed  = tool.GeneratePreprocessedFile;
			string asmloc            = tool.AssemblerListingLocation;
			string objFileLocation   = tool.ObjectFile;

			string generatedFile;
			if (mode == 1)
			{
				// asmListingAsmSrc => '.asm'
				generatedFile = Path.GetTempFileName() + ".asm"; //System.IO.Path.GetTempPath

				tool.WholeProgramOptimization = false;
				tool.AssemblerOutput = (dynamic)Enum.Parse(tool.AssemblerOutput.GetType(), "asmListingAsmSrc");
				tool.AssemblerListingLocation = generatedFile;
			}
			else /*if (mode == 2)*/
			{
				// not generally applicable
				//generatedFile = prj.ProjectDirectory + prjconfig.IntermediateDirectory + Replace(file.Name, ".cpp", ".i");
				generatedFile = Path.GetTempFileName() + ".cpp";

				tool.GeneratePreprocessedFile = (dynamic)Enum.Parse(tool.GeneratePreprocessedFile.GetType(), "preprocessYes");
				// there's no separate option for this, so misuse /Fo
				tool.ObjectFile = generatedFile;
			}

			try
			{
				// forceBuild (even if up-to-date) and waitOnBuild
				fileconfig.Compile(true, true);
			}
			catch (Exception e)
			{
				package.writeStatus($"VCFileConfiguration.Compile() failed: {e.Message}. Trying command Build.Compile now...");

				_dispBuildEvents_OnBuildProjConfigDoneEventHandler onProjBuildDone = null;
				onProjBuildDone = (project, projectConfig, platfrm, solutionConfig, success) =>
				{
					dte.Events.BuildEvents.OnBuildProjConfigDone -= onProjBuildDone;
					// naive cleanup
					if (mode == 1)
					{
						tool.WholeProgramOptimization = lto;
						tool.AssemblerOutput          = asmtype;
						tool.AssemblerListingLocation = asmloc;
					}
					else if (mode == 2)
					{
						tool.GeneratePreprocessedFile = genPreprocessed;
						tool.ObjectFile               = objFileLocation;
					}

					if (success)
						postCompileCpp(generatedFile, mode, functionOfInterest, curCodeLine);
				};
				dte.Events.BuildEvents.OnBuildProjConfigDone += onProjBuildDone;
				dte.ExecuteCommand("Build.Compile");
				return;
			}

			// naive cleanup
			if (mode == 1)
			{
				tool.WholeProgramOptimization = lto;
				tool.AssemblerOutput          = asmtype;
				tool.AssemblerListingLocation = asmloc;
			}
			else if (mode == 2)
			{
				tool.GeneratePreprocessedFile = genPreprocessed;
				tool.ObjectFile               = objFileLocation;
			}

			postCompileCpp(generatedFile, mode, functionOfInterest, curCodeLine);
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
				var input = new System.IO.StreamReader(generatedFile);
				generatedFile = System.IO.Path.GetTempFileName() + ".cpp";
				var output = new System.IO.StreamWriter(generatedFile);

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
			Window tmp = dte.ItemOperations.OpenFile(generatedFile, EnvDTE.Constants.vsViewKindCode);
			TextDocument genFileWindow = (TextDocument)tmp.Document.Object("TextDocument");

			// crashes VS
			//			bool ddd = genFileWindow.ReplacePattern("^$\n", "", (int)vsFindOptions.vsFindOptionsRegularExpression);
			// http://stackoverflow.com/questions/12453160/remove-empty-lines-in-text-using-visual-studio
			// ^:b*$\n -> ^(?([^\r\n])\s)*\r?$\r?\n

			// now try to find the function the user was looking at

			// if it's a template the fullName will be like ns::bar<T>
			// try to find an instantiation instead then
			int bracketPos = functionOfInterest.IndexOf("<");
			if (bracketPos > 0)
				functionOfInterest = functionOfInterest.Substring(0, bracketPos + 1);

			TextSelection textSelObj = genFileWindow.Selection;
			// first try to find the function
			// TODO: for some reason vsFindOptions.vsFindOptionsFromStart option doesn't work
			textSelObj.StartOfDocument();
			bool res = textSelObj.FindText("; " + functionOfInterest, (int)vsFindOptions.vsFindOptionsMatchCase);
			if (!res && mode == 1)
			{
				dte.StatusBar.Text = "Couldn't find function '" + functionOfInterest + "'";
				dte.StatusBar.Highlight(true);
			}

			// then search for the code line
			// it might not be there if it's optimized away
			if (!String.IsNullOrWhiteSpace(curCodeLine))
				textSelObj.FindText(curCodeLine, (int)vsFindOptions.vsFindOptionsMatchCase);

			textSelObj.StartOfLine();
		}

		/// if the current document is an .h file, at least try switching to a potential cpp file of the same name
		public void switchToCppFile()
		{
			DTE2 dte = this.dte;
			string origFile = dte.ActiveDocument.FullName;
			if (!Regex.IsMatch(origFile, @"\.h(?:pp)?$"))
				return;

			string altFile = Regex.Replace(origFile, @"\.h(?:pp)?$", ".cpp");
			if (!System.IO.File.Exists(altFile))
			{
				altFile = Regex.Replace(origFile, @"\.h(?:pp)?$", ".cc");
				if (!System.IO.File.Exists(altFile))
					throw new System.Exception("Couldn't find the cpp file corresponding to this header!");
			}
			dte.Documents.Open(altFile, "Text");
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

			// get currently viewed function
			string functionOfInterest = "";
			var selection = doc.Selection as TextSelection;
			CodeElement codeEl = selection.ActivePoint.CodeElement[vsCMElement.vsCMElementFunction];

			if (codeEl == null)
			{
				package.showMsgBox("You should place the cursor inside a function.");
				return;
			}

			functionOfInterest = codeEl.FullName;

			int line = selection.ActivePoint.Line;
			EditPoint editPoint = tdoc.CreateEditPoint();
			string curCodeLine = editPoint.GetLines(line, line + 1);

			string args = @"/language:C# /clearList /navigateTo:M:"; // /saveDir:%TEMP%\dddd";
			args += functionOfInterest;
			if (curCodeLine.Trim().Length > 5)
				args += " /search:\"" + curCodeLine + '"';

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
