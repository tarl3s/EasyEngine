using System.Windows;

namespace EasyEngine
{
    public partial class InputDialog : Window
    {
        public string Value => Input.Text.Trim();

        public InputDialog(string title, string prompt, string initial = "")
        {
            InitializeComponent();
            Title = title;
            PromptText.Text = prompt;
            Input.Text = initial;
            Loaded += (s, e) => { Input.Focus(); Input.SelectAll(); };
        }

        private void Ok_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
        private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

        /// <summary>Shows the dialog and returns the typed text, or null if cancelled/empty.</summary>
        public static string? Prompt(Window owner, string title, string prompt, string initial = "")
        {
            var dlg = new InputDialog(title, prompt, initial) { Owner = owner };
            if (dlg.ShowDialog() == true && dlg.Value.Length > 0) return dlg.Value;
            return null;
        }
    }
}
