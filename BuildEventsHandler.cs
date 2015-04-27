using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VSPackage.DevUtils
{
	internal class BuildEventsHandler
	{
		DevUtilsPackage _vspkg;
		// got to cache this object to get events
		BuildEvents _buildEvents;
		DateTime _buildStartTime;

		public BuildEventsHandler(DevUtilsPackage pkg)
		{
			_vspkg = pkg;
			_buildEvents = pkg.dte.Events.BuildEvents;
			_buildEvents.OnBuildBegin += buildStarted;
			_buildEvents.OnBuildDone += buildDone;
		}

#region Event Handlers
		private void buildStarted(vsBuildScope scope, vsBuildAction action)
		{
			if (scope != vsBuildScope.vsBuildScopeSolution)
				return;

			_buildStartTime = DateTime.Now;
		}

		private void buildDone(vsBuildScope scope, vsBuildAction action)
		{
			if (scope != vsBuildScope.vsBuildScopeSolution)
				return;

			string msg = String.Format("Total build time: {0}", DateTime.Now - _buildStartTime);
			_vspkg.writeToBuildWindow("\n" + msg);
			_vspkg.writeStatus(msg);
		}
#endregion
	}
}
