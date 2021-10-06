using System.Collections.Generic;

namespace DictionaryDemo
{
	class Program
	{
		static void Main(string[] args)
		{
			var dict = new Dictionary<string, string>();

			dict.Add("11", "Tserunyan");
			dict.Add("Atabek", "Doe");
			dict.Add("Fendy", "John");
			dict.Add("Mosh", "Holly");

			System.Console.WriteLine();
			//dict.Remove(11);

			//dict[32] = "hello";
		}
	}
}
