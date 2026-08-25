namespace LabDesktop.Client.App.Controls;

internal sealed class PasswordRevealButton : Button
{
    private bool _passwordVisible;

    public PasswordRevealButton()
    {
        AccessibleName = "显示密码";
        AccessibleRole = AccessibleRole.PushButton;
        BackColor = SystemColors.Window;
        Cursor = Cursors.Hand;
        Font = new Font("Segoe MDL2 Assets", 10F, FontStyle.Regular, GraphicsUnit.Point);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        FlatAppearance.MouseDownBackColor = Color.FromArgb(226, 232, 240);
        FlatAppearance.MouseOverBackColor = Color.FromArgb(241, 245, 249);
        ForeColor = Color.FromArgb(100, 116, 139);
        Margin = Padding.Empty;
        TabStop = true;
        Text = "\uE890";
        UseCompatibleTextRendering = false;
    }

    public bool PasswordVisible
    {
        get => _passwordVisible;
        private set
        {
            if (_passwordVisible == value)
            {
                return;
            }

            _passwordVisible = value;
            AccessibleName = value ? "隐藏密码" : "显示密码";
            ForeColor = value
                ? Color.FromArgb(37, 99, 235)
                : Color.FromArgb(100, 116, 139);
            PasswordVisibilityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? PasswordVisibilityChanged;

    internal void TogglePasswordVisibility() => PasswordVisible = !PasswordVisible;

    protected override void OnClick(EventArgs eventArgs)
    {
        TogglePasswordVisibility();
        base.OnClick(eventArgs);
    }
}
