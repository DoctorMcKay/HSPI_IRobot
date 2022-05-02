namespace HSPI_IRobot {
	internal class Program {
		public static void Main(string[] args) {
			HSPI plugin = new HSPI();
			plugin.Connect(args);
		}
	}
}
