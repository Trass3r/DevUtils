using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.ComponentModel.Design;
using Microsoft.Win32;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;

using EnvDTE;
using EnvDTE80;
using System.Text.RegularExpressions;

namespace VSPackage.DevUtils
{
	/// <summary>
	/// This is the class that implements the package exposed by this assembly.
	///
	/// The minimum requirement for a class to be considered a valid package for Visual Studio
	/// is to implement the IVsPackage interface and register itself with the shell.
	/// This package uses the helper classes defined inside the Managed Package Framework (MPF)
	/// to do it: it derives from the Package class that provides the implementation of the 
	/// IVsPackage interface and uses the registration attributes defined in the framework to 
	/// register itself and its components with the shell.
	/// </summary>
	// This attribute tells the PkgDef creation utility (CreatePkgDef.exe) that this class is
	// a package.
	[PackageRegistration(UseManagedResourcesOnly = true)]
	// This attribute is used to register the information needed to show this package
	// in the Help/About dialog of Visual Studio.
	[InstalledProductRegistration("#110", "#112", "0.3", IconResourceID = 400)]
	// This attribute is needed to let the shell know that this package exposes some menus.
	[ProvideMenuResource("Menus.ctmenu", 1)]
	// automatic module initialization when opening a solution file
	[ProvideAutoLoad(UIContextGuids80.SolutionExists)]
	[Guid(GuidList.guidDevUtilsPkgString)]
	internal sealed class DevUtilsPackage : Package
	{
		private BuildEventsHandler _buildEventsHandler;

		/// <summary>
		/// Default constructor of the package.
		/// Inside this method you can place any initialization code that does not require 
		/// any Visual Studio service because at this point the package object is created but 
		/// not sited yet inside Visual Studio environment. The place to do all the other 
		/// initialization is the Initialize method.
		/// </summary>
		public DevUtilsPackage()
		{
		}

		/// get the DTE object for this package
		public DTE2 dte
		{
			// retrieving it in Initialize is buggy cause the IDE might not be fully initialized then -.-
			// and it's not worth the hassle registering for ready events
			get { return (DTE2)GetService(typeof(DTE)); }
		}

		/////////////////////////////////////////////////////////////////////////////
		// Overridden Package Implementation
		#region Package Members

		/// <summary>
		/// Initialization of the package; this method is called right after the package is sited, so this is the place
		/// where you can put all the initialization code that rely on services provided by VisualStudio.
		/// </summary>
		protected override void Initialize()
		{
			Debug.WriteLine(string.Format(CultureInfo.CurrentCulture, "Entering Initialize() of: {0}", this.ToString()));
			base.Initialize();

			_buildEventsHandler = new BuildEventsHandler(this);

			// Add our command handlers for menu (commands must exist in the .vsct file)
			OleMenuCommandService mcs = GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
			if (null != mcs)
			{
				// Create the command for the menu item.
				CommandID menuCommandID = new CommandID(GuidList.guidDevUtilsCmdSet, (int)PkgCmdIDList.cmdShowAssembly);
				var cmd = new OleMenuCommand(showAssembly, changeHandler, beforeQueryStatus, menuCommandID);
				mcs.AddCommand(cmd);

				menuCommandID = new CommandID(GuidList.guidDevUtilsCmdSet, (int)PkgCmdIDList.cmdShowPreprocessed);

				cmd = new OleMenuCommand(showPreprocessed, changeHandler, beforeQueryStatus, menuCommandID);
				mcs.AddCommand(cmd);
			}
		}
		#endregion

		private void changeHandler(object sender, EventArgs e)
		{
		}

		/// called when the context menu is opened
		///
		/// makes entries only visible if the document is actually C++
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

			bool enabled = false;
			bool visible = true;

			ProjectItem projectItem = doc != null ? doc.ProjectItem : null;
			if (projectItem != null)
			{
				if (doc.Language != "C/C++")
					visible = false;
				else if (projectItem.Object != null && projectItem.ContainingProject != null)
					enabled = projectItem.ContainingProject.Object != null;
			}

			menuCommand.Enabled = enabled;
			menuCommand.Visible = visible;
		}

		// callback: show assembly code for currently open source file
		private void showAssembly(object sender, EventArgs e)
		{
			doIt(1);
		}

		// callback: show preprocessed source code
		private void showPreprocessed(object sender, EventArgs e)
		{
			doIt(2);
		}

		// TODO: mode 1 is assembly, 2 is preprocessed
		private void doIt(int mode = 1)
		{
			// if in a header try to open the cpp file
			switchToCppFile();

			// get the currently active document from the IDE
			Document doc = dte.ActiveDocument;
			TextDocument tdoc = doc.Object("TextDocument") as TextDocument;

			if (tdoc == null)
			{
				showMsgBox("Could not obtain the active TextDocument object");
				return;
			}

			// get the name of the document (lower-case)
			string docname = doc.Name.ToLower();

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

			// TODO: TextSelection or EditPoint?
			// http://www.viva64.com/en/a/0082/#ID0ELOBK
			// EditPoint directly manipulates text buffer data instead of operating with the text through the editor UI.
			// The difference between them is that the text buffer is not influenced by such editor-specific notions as WordWrap and Virtual Spaces.
			int line = selection.ActivePoint.Line;
			TextDocument textDoc = doc.Object() as TextDocument;
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
				generatedFile = System.IO.Path.GetTempFileName() + ".asm"; //System.IO.Path.GetTempPath

				tool.WholeProgramOptimization = false;
				tool.AssemblerOutput = (dynamic)Enum.Parse(tool.AssemblerOutput.GetType(), "asmListingAsmSrc");
				tool.AssemblerListingLocation = generatedFile;
			}
			else /*if (mode == 2)*/
			{
				// not generally applicable
				//generatedFile = prj.ProjectDirectory + prjconfig.IntermediateDirectory + Replace(file.Name, ".cpp", ".i");
				generatedFile = System.IO.Path.GetTempFileName() + ".cpp";

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
				showMsgBox("Compilation failed, this means there are errors in the code:\n" + e.Message);
				return;
			}
			finally
			{
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
			}

			// clean the preprocessed output
			// TODO: do this in a better way
			if (mode == 2)
			{
				var input     = new System.IO.StreamReader(generatedFile);
				generatedFile = System.IO.Path.GetTempFileName() + ".cpp";
				var output    = new System.IO.StreamWriter(generatedFile);
				
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
				functionOfInterest = functionOfInterest.Substring(0, bracketPos+1);

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
			DTE2 dte = (DTE2)GetService(typeof(DTE));
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

		private void showMsgBox(string msg, string title = "Error")
		{
			IVsUIShell uiShell = (IVsUIShell)GetService(typeof(SVsUIShell));
			Guid clsid = Guid.Empty;
			int result;
			uiShell.ShowMessageBox(0,
			                       ref clsid,
			                       title,
			                       msg,
			                       string.Empty,
			                       0,
			                       OLEMSGBUTTON.OLEMSGBUTTON_OK,
			                       OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST,
			                       OLEMSGICON.OLEMSGICON_INFO,
			                       0, // false
			                       out result);
		}

		public void writeStatus(string msg)
		{
			IVsStatusbar sb = (IVsStatusbar) GetService(typeof(SVsStatusbar));
			sb.SetColorText(msg, 0, 0);
		}

		public void writeToBuildWindow(string msg)
		{
			DTE2 dte = (DTE2)GetService(typeof(DTE));
			var win = dte.Windows.Item(EnvDTE.Constants.vsWindowKindOutput);
			var ow = win.Object as OutputWindow;
			if (ow == null)
				return;

			foreach (OutputWindowPane owPane in ow.OutputWindowPanes)
			{
				if (owPane.Name == "Build")
				{
					owPane.OutputString(msg);
					break;
				}
			}
		}
	}
}
