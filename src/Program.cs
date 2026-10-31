namespace Win4DaveMediaPlayer;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            Application.Run(new MainForm(args.FirstOrDefault()));
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Win4DaveMediaPlayer",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
