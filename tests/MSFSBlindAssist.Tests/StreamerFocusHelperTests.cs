using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Controls;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class StreamerFocusHelperTests
{
    private static void RunOnSta(Action action)
    {
        Exception? ex = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                ex = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (ex != null)
            throw ex;
    }

    [Fact]
    public void CreateFocusCheckBox_SetsAccessibilityProperties()
    {
        RunOnSta(() =>
        {
            using var form = new Form();
            SettingsManager.Current.StreamerModeEnabled = true;

            var checkBox = StreamerCameraFocusHelper.CreateFocusCheckBox(form, CockpitCameraTarget.MCDU, new Point(10, 10));

            Assert.Equal("Focus camera here", checkBox.Text);
            Assert.Equal("Focus camera here", checkBox.AccessibleName);
            Assert.Contains("Alt+Shift+C", checkBox.AccessibleDescription);
            Assert.True(checkBox.Visible);
            Assert.Equal(new Point(10, 10), checkBox.Location);
        });
    }

    [Fact]
    public void CreateFocusCheckBox_WhenStreamerModeDisabled_IsHidden()
    {
        RunOnSta(() =>
        {
            using var form = new Form();
            SettingsManager.Current.StreamerModeEnabled = false;

            var checkBox = StreamerCameraFocusHelper.CreateFocusCheckBox(form, CockpitCameraTarget.EFB, new Point(0, 0));

            Assert.False(checkBox.Visible);
        });
    }

    [Fact]
    public void HandleShortcut_WhenVisible_TogglesChecked()
    {
        RunOnSta(() =>
        {
            using var checkBox = new CheckBox { Visible = true, Checked = false };

            bool handled = StreamerCameraFocusHelper.HandleShortcut(Keys.Alt | Keys.Shift | Keys.C, checkBox);

            Assert.True(handled);
            Assert.True(checkBox.Checked);

            bool handledAgain = StreamerCameraFocusHelper.HandleShortcut(Keys.Alt | Keys.Shift | Keys.C, checkBox);
            Assert.True(handledAgain);
            Assert.False(checkBox.Checked);
        });
    }

    [Fact]
    public void HandleShortcut_WhenNotVisible_ReturnsFalse()
    {
        RunOnSta(() =>
        {
            using var checkBox = new CheckBox { Visible = false, Checked = false };

            bool handled = StreamerCameraFocusHelper.HandleShortcut(Keys.Alt | Keys.Shift | Keys.C, checkBox);

            Assert.False(handled);
            Assert.False(checkBox.Checked);
        });
    }

    [Fact]
    public void HandleShortcut_WhenDifferentKey_ReturnsFalse()
    {
        RunOnSta(() =>
        {
            using var checkBox = new CheckBox { Visible = true, Checked = false };

            bool handled = StreamerCameraFocusHelper.HandleShortcut(Keys.Alt | Keys.F, checkBox);

            Assert.False(handled);
            Assert.False(checkBox.Checked);
        });
    }

    [Fact]
    public void FormClosing_WhenChecked_UnchecksBox()
    {
        RunOnSta(() =>
        {
            using var form = new Form();
            SettingsManager.Current.StreamerModeEnabled = true;
            var checkBox = StreamerCameraFocusHelper.CreateFocusCheckBox(form, CockpitCameraTarget.MCDU, Point.Empty);
            form.Controls.Add(checkBox);
            form.Show();
            checkBox.Checked = true;

            form.Close();

            Assert.False(checkBox.Checked);
        });
    }

    [Fact]
    public void FormDisposed_WhenChecked_UnchecksBox()
    {
        RunOnSta(() =>
        {
            var form = new Form();
            SettingsManager.Current.StreamerModeEnabled = true;
            var checkBox = StreamerCameraFocusHelper.CreateFocusCheckBox(form, CockpitCameraTarget.MCDU, Point.Empty);
            form.Controls.Add(checkBox);
            checkBox.Checked = true;

            form.Dispose();

            Assert.False(checkBox.Checked);
        });
    }
}

