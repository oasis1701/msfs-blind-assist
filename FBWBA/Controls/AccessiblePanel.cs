
namespace FBWBA.Controls;

public class AccessiblePanel : Panel
{
    public AccessiblePanel()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
    }

    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        Invalidate();
    }

    protected override void OnLeave(EventArgs e)
    {
        base.OnLeave(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (Focused)
        {
            ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
        }
    }

    protected override bool IsInputKey(Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Up:
            case Keys.Down:
            case Keys.Left:
            case Keys.Right:
            case Keys.Tab:
                return true;
        }
        return base.IsInputKey(keyData);
    }
}
