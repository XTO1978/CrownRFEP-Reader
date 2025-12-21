using ObjCRuntime;
using UIKit;
using System;

namespace CrownRFEP_Reader;

public class Program
{
	// This is the main entry point of the application.
	static void Main(string[] args)
	{
		// Capturar excepciones no controladas para diagnóstico
		AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
		{
			var ex = e.ExceptionObject as Exception;
			System.Diagnostics.Debug.WriteLine("=== EXCEPCIÓN NO CONTROLADA ===");
			System.Diagnostics.Debug.WriteLine($"Tipo: {ex?.GetType().FullName}");
			System.Diagnostics.Debug.WriteLine($"Mensaje: {ex?.Message}");
			System.Diagnostics.Debug.WriteLine($"StackTrace: {ex?.StackTrace}");
			if (ex?.InnerException != null)
			{
				System.Diagnostics.Debug.WriteLine($"Inner: {ex.InnerException.Message}");
				System.Diagnostics.Debug.WriteLine($"Inner StackTrace: {ex.InnerException.StackTrace}");
			}
			Console.WriteLine("=== EXCEPCIÓN NO CONTROLADA ===");
			Console.WriteLine($"Tipo: {ex?.GetType().FullName}");
			Console.WriteLine($"Mensaje: {ex?.Message}");
			Console.WriteLine($"StackTrace: {ex?.StackTrace}");
		};

		Runtime.MarshalObjectiveCException += (sender, e) =>
		{
			System.Diagnostics.Debug.WriteLine("=== ObjC EXCEPTION ===");
			System.Diagnostics.Debug.WriteLine($"ObjC Exception: {e.Exception}");
			Console.WriteLine("=== ObjC EXCEPTION ===");
			Console.WriteLine($"ObjC Exception: {e.Exception}");
		};

		// if you want to use a different Application Delegate class from "AppDelegate"
		// you can specify it here.
		UIApplication.Main(args, null, typeof(AppDelegate));
	}
}
