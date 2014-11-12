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
//using System.Windows.Forms;
using Microsoft.VisualStudio.VCProjectEngine;
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
	[InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)]
	// This attribute is needed to let the shell know that this package exposes some menus.
	[ProvideMenuResource("Menus.ctmenu", 1)]
	// automatic module initialization when opening a solution file
	[ProvideAutoLoad(UIContextGuids80.SolutionExists)]
	[Guid(GuidList.guidDevUtilsPkgString)]
	public sealed class DevUtilsPackage : Package
	{
		BuildEventsHandler _buildEventsHandler;
		DTE2 _dte;

		/// <summary>
		/// Default constructor of the package.
		/// Inside this method you can place any initialization code that does not require 
		/// any Visual Studio service because at this point the package object is created but 
		/// not sited yet inside Visual Studio environment. The place to do all the other 
		/// initialization is the Initialize method.
		/// </summary>
		public DevUtilsPackage()
		{
//			Debug.WriteLine(string.Format(CultureInfo.CurrentCulture, "Entering constructor for: {0}", this.ToString()));
		}

		/// get the DTE object for this package
		public DTE2 dte
		{
			get { return _dte; }
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

			_dte = (DTE2)GetService(typeof(DTE));
			_buildEventsHandler = new BuildEventsHandler(this);

			// Add our command handlers for menu (commands must exist in the .vsct file)
			OleMenuCommandService mcs = GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
			if (null != mcs)
			{
				// Create the command for the menu item.
				CommandID menuCommandID = new CommandID(GuidList.guidDevUtilsCmdSet, (int)PkgCmdIDList.cmdShowAssembly);
				MenuCommand menuItem = new MenuCommand(showAssembly, menuCommandID);
				mcs.AddCommand(menuItem);

				menuCommandID = new CommandID(GuidList.guidDevUtilsCmdSet, (int)PkgCmdIDList.cmdShowPreprocessed);
				menuItem = new MenuCommand(showPreprocessed, menuCommandID);
				mcs.AddCommand(menuItem);
			}
		}
		#endregion

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
			// use together with https://asmhighlighter.codeplex.com/releases

			// retrieve it here since IDE might not be fully initialized when calling Initialize -.-
			// and it' not worth the hassle registering for ready events
			DTE2 dte = (DTE2)GetService(typeof(DTE));

			// if in a header try to open the cpp file
			switchToCppFile();

			// get the currently active document from the IDE
			Document doc = dte.ActiveDocument;
			TextDocument tdoc = dte.ActiveDocument.Object("TextDocument") as TextDocument;

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

			// get the project that contains this document
			VCProject prj = doc.ProjectItem.ContainingProject.Object as VCProject;

			if (prj == null)
			{
				showMsgBox("This file doesn't seem to be part of a VC++ project");
				return;
			}

			// get current configuration
			Project p = doc.ProjectItem.ContainingProject;
			ConfigurationManager mgr = p.ConfigurationManager;
			string platform = mgr.ActiveConfiguration.PlatformName;
			string conf = mgr.ActiveConfiguration.ConfigurationName;

/*
			VCProjectItem docItem = doc.ProjectItem.Object as VCProjectItem;
			IVCCollection prjconfigs = prj.Configurations;
			VCConfiguration prjconfig = prjconfigs.Item(conf + "|" + platform) as VCConfiguration;
			VCCLCompilerTool prjtool = prjconfig.Tools.Item("VCCLCompilerTool") as VCCLCompilerTool;
 */

			// save the project file
			//prj.save();

			SolutionBuild sb = dte.Solution.SolutionBuild;
			string acc = sb.ActiveConfiguration.Name;

			// find the currently active configuration for the current file
			VCFile file                    = doc.ProjectItem.Object as VCFile;
			IVCCollection fileconfigs      = file.FileConfigurations as IVCCollection;
			VCFileConfiguration fileconfig = fileconfigs.Item(conf + "|" + platform) as VCFileConfiguration;
			VCCLCompilerTool tool          = fileconfig.Tool;

			// save original settings
			bool lto                 = tool.WholeProgramOptimization;
			asmListingOption asmtype = tool.AssemblerOutput;
			string asmloc            = tool.AssemblerListingLocation;
			string objFileLocation   = tool.ObjectFile;

			// undo context doesn't seem to work for project settings
//			dte.UndoContext.Open("ModifiedProjectSettings");

			string generatedFile;
			if (mode == 1)
			{
				// asmListingAsmSrc => '.asm'
				generatedFile = System.IO.Path.GetTempFileName() + ".asm"; //System.IO.Path.GetTempPath

				tool.WholeProgramOptimization = false;
				tool.AssemblerOutput = asmListingOption.asmListingAsmSrc;
				tool.AssemblerListingLocation = generatedFile;
			}
			else /*if (mode == 2)*/
			{
				// not generally applicable
				//generatedFile = prj.ProjectDirectory + prjconfig.IntermediateDirectory + Replace(file.Name, ".cpp", ".i");
				generatedFile = System.IO.Path.GetTempFileName() + ".cpp";

				tool.GeneratePreprocessedFile = preprocessOption.preprocessYes;
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
/*
					// clean up
				// try reloading. https://blogs.msdn.com/b/jjameson/archive/2009/03/11/visual-studio-macros-for-unloading-reloading-projects.aspx
				Window solutionExplorer = dte.Windows.Item(Constants.vsWindowKindSolutionExplorer);
				solutionExplorer.Activate();
				string solutionName = dte.Solution.Properties.Item("Name").Value.ToString();
				string projPath = solutionName + "\\" + prj.Name;
				UIHierarchy solutionHierarchy = (UIHierarchy)solutionExplorer;
				UIHierarchyItem projUIItem = solutionHierarchy.GetItem(projPath);
				projUIItem.Select(vsUISelectionType.vsUISelectionTypeSelect);
				dte.ExecuteCommand("Project.UnloadProject");
				dte.ExecuteCommand("Project.ReloadProject");
*/

				// naive cleanup
				if (mode == 1)
				{
					// switch back to prior config
//					if (conf != "Release")
//						sb.SolutionConfigurations.Item(conf).Activate();

					tool.WholeProgramOptimization = lto;
					tool.AssemblerOutput = asmtype;
					tool.AssemblerListingLocation = asmloc;
				}
				else if (mode == 2)
				{
					tool.GeneratePreprocessedFile = preprocessOption.preprocessNo;
					tool.ObjectFile = objFileLocation;
				}
			}
/*
			}
			catch(Exception)
			finally
			{
				dte.UndoContext.SetAborted();
				// seems like SetAborted already closes the context
				if (dte.UndoContext.IsOpen())
					dte.UndoContext.Close();
			}
*/

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

			// now try to find the function the user was looking at
/*
			dte.ExecuteCommand("Edit.Find");
			dte.Find.Backwards = false;
			dte.Find.MatchCase = true;
			dte.Find.FindWhat = functionOfInterest + ", COMDAT";
			dte.Find.Target = vsFindTarget.vsFindTargetCurrentDocument;
			dte.Find.PatternSyntax = vsFindPatternSyntax.vsFindPatternSyntaxLiteral;
			dte.Find.Action = vsFindAction.vsFindActionFind;
*/

			// first try to find the function, should work
//			vsFindResult funcFound = dte.Find.Execute();

			// now try to jump to the line
			// close the find window if we found at least the function
/*
			if (!String.IsNullOrWhiteSpace(curCodeLine))
			{
			    dte.Find.FindWhat = curCodeLine;
			    dte.Find.Execute();
			}
			// close find window if found
			if (funcFound != vsFindResult.vsFindResultNotFound)
				dte.Windows.Item("{CF2DDC32-8CAD-11D2-9302-005345000000}").Close();
*/

			// crashes VS
//			bool ddd = genFileWindow.ReplacePattern("^$\n", "", (int)vsFindOptions.vsFindOptionsRegularExpression);
			// http://stackoverflow.com/questions/12453160/remove-empty-lines-in-text-using-visual-studio
			// ^:b*$\n -> ^(?([^\r\n])\s)*\r?$\r?\n

			TextSelection textSelObj = genFileWindow.Selection;
			// first try to find the function
			// TODO: for some reason vsFindOptions.vsFindOptionsFromStart option doesn't work
			textSelObj.StartOfDocument();
			bool res = textSelObj.FindText(functionOfInterest + ", COMDAT", (int)vsFindOptions.vsFindOptionsMatchCase);
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
