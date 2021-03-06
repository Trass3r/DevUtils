﻿using System;
using System.Runtime.InteropServices;
using System.Threading;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace DevUtils
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
	[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
	// This attribute is used to register the information needed to show this package
	// in the Help/About dialog of Visual Studio.
	[InstalledProductRegistration("#110", "#112", "0.7.0", IconResourceID = 400)]
	// This attribute is needed to let the shell know that this package exposes some menus.
	[ProvideMenuResource("Menus.ctmenu", 1)]
	// automatic module initialization when opening a solution file
	[ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
	[Guid(DevUtilsPackage.GUID)]
	internal sealed class DevUtilsPackage : AsyncPackage
	{
		public const string GUID = "19b8a882-47f2-4fdd-a657-5f15a2c5ecae";
		private BuildEventsHandler _buildEventsHandler;
		private IVsStatusbar _statusBar;

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

		// get the DTE object for this package
		// retrieving it in Package.Initialize is buggy cause the IDE might not be fully initialized then -.-
		// and it's not worth the hassle registering for ready events
		internal DTE2 dte
		{
			get;
			set;
		}

		/////////////////////////////////////////////////////////////////////////////
		// Overridden Package Implementation
		#region Package Members

		/// <summary>
		/// Initialization of the package; this method is called right after the package is sited, so this is the place
		/// where you can put all the initialization code that rely on services provided by VisualStudio.
		/// </summary>
		protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
		{
			dte = await GetServiceAsync(typeof(DTE)) as DTE2;
			_statusBar = await GetServiceAsync(typeof(SVsStatusbar)) as IVsStatusbar;

			// switch to the UI thread for command initialization
			await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

			_buildEventsHandler = new BuildEventsHandler(this);
			CompilerOutputCmds.Initialize(this);
		}
		#endregion

		public void showMsgBox(string msg, string title = "Error")
		{
			VsShellUtilities.ShowMessageBox(
				this,
				msg,
				title,
				OLEMSGICON.OLEMSGICON_INFO,
				OLEMSGBUTTON.OLEMSGBUTTON_OK,
				OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
		}

		public void writeStatus(string msg)
		{
			_statusBar.SetColorText(msg, 0, 0);
		}

		public void writeToBuildWindow(string msg)
		{
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
