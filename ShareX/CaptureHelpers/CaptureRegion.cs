#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

using ShareX.HelpersLib;
using ShareX.ScreenCaptureLib;
using ShareX.ScreenCaptureLib.Presentation.RegionCapture;
using System.Drawing;
using System.Threading.Tasks;

namespace ShareX
{
    public class CaptureRegion : CaptureBase
    {
        protected override TaskMetadata Execute(TaskSettings taskSettings)
        {
            return null;
        }

        protected override async Task<TaskMetadata> ExecuteAsync(TaskSettings taskSettings)
        {
            return await ExecuteRegionCaptureAvaloniaAsync(taskSettings);
        }

        protected async Task<TaskMetadata> ExecuteRegionCaptureAvaloniaAsync(TaskSettings taskSettings)
        {
            bool activeMonitorMode = taskSettings.CaptureSettings.RegionCaptureOptions.ActiveMonitorMode;
            Rectangle screenBounds = activeMonitorMode
                ? CaptureHelpers.GetActiveScreenBounds()
                : CaptureHelpers.GetScreenBounds();

            AvaloniaRegionCaptureRequest request = new AvaloniaRegionCaptureRequest
            {
                ScreenBounds = screenBounds,
                RegionCaptureOptions = taskSettings.CaptureSettingsReference.RegionCaptureOptions,
                ImageEditorOptions = taskSettings.ToolsSettingsReference.ImageEditorOptions,
                EnableAnnotations = !taskSettings.AdvancedSettings.RegionCaptureDisableAnnotation,
                AllowMultipleRegions = true
            };

            AvaloniaRegionCaptureResult result = await RegionCaptureIntegration.CaptureAsync(request,
                pending => GrabScreen(pending, taskSettings, activeMonitorMode));
            if (result == null)
            {
                return null;
            }

            // GetSnapshot returns a bitmap nothing else references, so the task metadata takes it as is.
            TaskMetadata metadata = new TaskMetadata(result.Image);

            if (result.ImageModified)
            {
                AllowAnnotation = false;
            }

            if (result.WindowInfo != null)
            {
                metadata.UpdateInfo(result.WindowInfo);
            }

            return metadata;
        }

        // Runs on a worker thread while the capture window is built.
        private static void GrabScreen(AvaloniaRegionCaptureRequest request, TaskSettings taskSettings, bool activeMonitorMode)
        {
            Screenshot screenshot = TaskHelpers.GetScreenshot(taskSettings);
            screenshot.CaptureCursor = false;

            // The capture request owns this bitmap and disposes it, so no copy is needed.
            request.Screenshot = activeMonitorMode
                ? screenshot.CaptureActiveMonitor()
                : screenshot.CaptureFullscreen();

            if (taskSettings.CaptureSettings.ShowCursor)
            {
                CursorData cursorData = new CursorData();
                if (cursorData.IsVisible)
                {
                    Rectangle screenBounds = request.ScreenBounds;
                    request.CursorBitmap = cursorData.ToBitmap(request.Screenshot, screenBounds.Location);
                    request.CursorPosition = new Point(
                        cursorData.DrawPosition.X - screenBounds.X,
                        cursorData.DrawPosition.Y - screenBounds.Y);
                }
            }
        }
    }
}
