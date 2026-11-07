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

#nullable enable

using Avalonia.Threading;
using ShareX.AvaloniaUI.Integration;
using ShareX.ImageEditor.Integration;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace ShareX.ScreenCaptureLib.Presentation.RegionCapture;

/// <summary>UI-thread boundary for the Avalonia region capture experience.</summary>
public static class RegionCaptureIntegration
{
    /// <summary>Bounds of the last captured region, or of all regions when several were captured.</summary>
    public static Rectangle LastRegionRectangle { get; private set; }

    /// <summary>Screen rectangles of the last captured regions. Holds more than one item after a multi region capture.</summary>
    public static IReadOnlyList<Rectangle> LastRegionRectangles { get; private set; } = [];

    internal static void SetLastRegion(Rectangle bounds, IReadOnlyList<Rectangle> regions)
    {
        LastRegionRectangle = bounds;
        LastRegionRectangles = regions;
    }

    /// <summary>
    /// Builds and discards a region capture window once, in the background, so the first capture after startup
    /// doesn't pay for loading its XAML, styles and editor types while the user waits.
    /// </summary>
    public static void Prewarm()
    {
        AvaloniaBootstrapper.EnsureInitialized();

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                ImageEditorIntegration.Initialize();

                // Default options: the user's options could lock the cursor (active monitor mode) when the window is built.
                AvaloniaRegionCaptureRequest request = new AvaloniaRegionCaptureRequest
                {
                    Screenshot = new SkiaSharp.SKBitmap(1, 1),
                    ScreenBounds = new Rectangle(0, 0, 1, 1),
                    RegionCaptureOptions = new RegionCaptureOptions(),
                    ImageEditorOptions = new ImageEditorOptions()
                };

                // Never shown; closing it disposes the request's bitmap and view model.
                new RegionCaptureWindow(request).Close();
            }
            catch (Exception ex)
            {
                ShareX.HelpersLib.DebugHelper.WriteException(ex, "Region capture prewarm failed.");
            }
        }, DispatcherPriority.Background);
    }

    /// <param name="request">Capture options. Ownership of its bitmaps transfers to the capture window.</param>
    /// <param name="grab">
    /// Optional screen grab that fills <see cref="AvaloniaRegionCaptureRequest.Screenshot"/> and the cursor. It runs on a
    /// worker thread while the UI thread builds the capture window, so the two overlap.
    /// </param>
    public static Task<AvaloniaRegionCaptureResult?> CaptureAsync(AvaloniaRegionCaptureRequest request,
        Action<AvaloniaRegionCaptureRequest>? grab = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        Task grabTask = grab == null ? Task.CompletedTask : Task.Run(() => grab(request));

        try
        {
            AvaloniaBootstrapper.EnsureInitialized();
        }
        catch (Exception ex)
        {
            return FailAsync(request, grabTask, ex);
        }

        TaskCompletionSource<AvaloniaRegionCaptureResult?> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        Dispatcher.UIThread.Post(async () =>
        {
            RegionCaptureWindow? window = null;

            try
            {
                ImageEditorIntegration.Initialize();
                window = new RegionCaptureWindow(request);
                await grabTask;
                completion.TrySetResult(await window.CaptureAsync());
            }
            catch (Exception ex)
            {
                if (window == null || !window.IsVisible)
                {
                    await DisposeBitmapsAsync(request, grabTask);
                }

                completion.TrySetException(ex);
            }
        });

        return completion.Task;
    }

    private static async Task<AvaloniaRegionCaptureResult?> FailAsync(AvaloniaRegionCaptureRequest request, Task grabTask,
        Exception exception)
    {
        await DisposeBitmapsAsync(request, grabTask);
        ExceptionDispatchInfo.Throw(exception);
        return null;
    }

    // Waits for a pending grab first, so bitmaps it creates after a failure are still released.
    private static async Task DisposeBitmapsAsync(AvaloniaRegionCaptureRequest request, Task grabTask)
    {
        try
        {
            await grabTask;
        }
        catch
        {
            // The caller reports the failure; only the cleanup matters here.
        }

        request.Screenshot?.Dispose();
        request.CursorBitmap?.Dispose();
    }

    /// <summary>
    /// Lets the user draw a rectangle over the live desktop and returns it in screen pixels, or null when cancelled.
    /// The selection window is closed when the task completes, so the caller can capture the screen right after.
    /// </summary>
    public static Task<Rectangle?> SelectTransparentRegionAsync(Rectangle screenBounds, bool lockCursorToWindow)
    {
        AvaloniaBootstrapper.EnsureInitialized();

        TaskCompletionSource<Rectangle?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                RegionCaptureTransparentWindow window = new(screenBounds, lockCursorToWindow);
                Rectangle? selection = await window.SelectAsync();

                if (selection is Rectangle rectangle)
                {
                    SetLastRegion(rectangle, [rectangle]);
                }

                completion.TrySetResult(selection);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });

        return completion.Task;
    }
}
