namespace BlazorTriggerBench;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		// Two tabs: the Blazor Hybrid bench, and a MAUI-native CollectionView bench (4th framework).
		var tabs = new TabbedPage
		{
			Children =
			{
				new MainPage { Title = "Blazor (Hybrid)" },
				new MauiBenchPage { Title = "MAUI 原生" },
			},
		};
		return new Window(tabs) { Title = "BlazorTriggerBench" };
	}
}
