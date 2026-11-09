namespace FocusWShell.TestWindows;

/// <summary>
/// Abre una ventana por argumento con el estado indicado:
/// <c>normal</c>, <c>maximized</c> o <c>minimized</c>.
/// Ejemplo: <c>FocusWShell.TestWindows normal maximized minimized</c>.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var states = args.Length > 0 ? args : ["normal"];
        var context = new ApplicationContext();
        int open = 0;
        for (int i = 0; i < states.Length; i++)
        {
            var form = CreateForm(i + 1, states[i]);
            open++;
            form.FormClosed += (_, _) =>
            {
                if (--open == 0)
                {
                    context.ExitThread();
                }
            };
            form.Show();
        }

        Application.Run(context);
    }

    private static Form CreateForm(int number, string state)
    {
        var form = new Form
        {
            Text = $"FocusWShell prueba {number} ({state}) · pid {Environment.ProcessId}",
            StartPosition = FormStartPosition.Manual,
            Location = new Point(200 + (number * 60), 150 + (number * 60)),
            Size = new Size(800, 600),
            BackColor = Color.FromArgb(40 + (number * 30) % 200, 90, 140),
            WindowState = state.ToLowerInvariant() switch
            {
                "maximized" => FormWindowState.Maximized,
                "minimized" => FormWindowState.Minimized,
                "normal" => FormWindowState.Normal,
                _ => throw new ArgumentException($"Estado desconocido: {state}"),
            },
        };
        form.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 18),
            Text = form.Text,
        });
        return form;
    }
}
