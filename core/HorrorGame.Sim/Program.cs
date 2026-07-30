using System;
using HorrorGame.Core;

namespace HorrorGame.Sim
{
    /// <summary>
    /// Entry point for the headless balance simulator.
    /// </summary>
    public static class Program
    {
        /// <summary>Runs the simulator.</summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>0 on success, non-zero on a validation failure.</returns>
        public static int Main(string[] args)
        {
            try
            {
                GameConstants.Validate();
            }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine("Balance constants are inconsistent: " + ex.Message);
                return 2;
            }

            return SimCommands.Run(args);
        }
    }
}
