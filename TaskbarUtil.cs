using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Shell;

namespace VSPackage.DevUtils
{
	class Taskbar
	{
		TaskbarItemInfo _tbinfo = new TaskbarItemInfo();

		/// Updates the taskbar status based on the current build conditions.
		private void updateStatus()
		{
			//ITaskbar
//			TaskbarItemProgressState progressState = TaskbarItemProgressState.None;

/*			//a failed build should always be indicated in the taskbar.
			if (HasBuildFailed)
			{
				progressState = TaskbarItemProgressState.Error;
			}
			else
			{
				if (IsBuildActive)
				{
					if (IsProgressIndeterminate)
					{
						progressState = TaskbarItemProgressState.Indeterminate;
					}
					else
					{
						progressState = TaskbarItemProgressState.Normal;
					}
				}
			}

			_tbinfo.ProgressState = progressState;
			_tbinfo.ProgressValue = ProgressPercentage;
*/		}

		/// remove progress
		internal void reset()
		{
			_tbinfo.ProgressState = TaskbarItemProgressState.None;
			_tbinfo.ProgressValue = 0;
		}
	}
}
