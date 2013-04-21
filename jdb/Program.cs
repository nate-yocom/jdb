using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Jint.Debugger;
using Jint.Native;
using LibJdb;

namespace jdb
{
    class Program
    {
        private static bool stepping = false;
        static bool CommandLoop(Jint.Expressions.Program program, DebugInformation information, JdbEngine debugger)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.BackgroundColor = ConsoleColor.Black;

            if (information == null)
            {
                information = new DebugInformation();
                information.CallStack = new Stack<string>();
                information.Locals = new JsObject(JsNull.Instance);

                foreach (var property in debugger.CurrentScope.GetKeys())
                    information.Locals[property] = debugger.CurrentScope[property];
            }
            else
            {
                Console.WriteLine("{0}:{1} => {2}", information.CurrentStatement.Source.Start.Line,
                                  information.CurrentStatement.Source.Start.Char,
                                  information.CurrentStatement.Source.Code);
            }

            try
            {
                while (true)
                {                    
                    Console.Write(">");
                    var command = Console.ReadLine().ToLowerInvariant();

                    if (command == "bt")
                    {
                        // backtrace    
                        int frame = 0;
                        foreach (string stackframe in information.CallStack)
                        {
                            Console.WriteLine("[{0}] {1}", frame++, stackframe);
                        }
                    }
                    else if (command.StartsWith("p "))
                    {
                        // find value for next word and print it
                        string varname = command.Substring(command.IndexOf(" ") + 1);
                        Console.WriteLine("{0} => {1}", varname, information.Locals[varname].Value);
                    }
                    else if (command == "l")
                    {
                        // locals
                        foreach (string key in information.Locals.GetKeys())
                        {
                            Console.WriteLine("{0} => {1}", key, information.Locals[key].Value);
                        }
                    }
                    else if (command == "n")
                    {
                        // Step
                        stepping = true;
                        return true;
                    }
                    else if (command == "c" || command == "r")
                    {
                        // continue
                        stepping = false;
                        return true;
                    }
                    else if (command == "q")
                    {
                        // quit
                        return false;
                    }
                    else if (command.StartsWith("bp "))
                    {
                        // set a breakpoint
                        string[] split = command.Split(new string[] {" "}, StringSplitOptions.RemoveEmptyEntries);
                        int line = 0;
                        int chr = 0;
                        string expr = null;
                        if (split.Length > 1)
                        {
                            line = int.Parse(split[1]);

                            if (split.Length > 2)
                            {
                                chr = int.Parse(split[2]);

                                if (split.Length > 3)
                                    expr = split[3];
                            }

                            debugger.BreakPoints.Add(new BreakPoint(line, chr, expr));
                        }
                    }
                    else if (command == "lbp")
                    {
                        // list breakpoints
                        int bpcount = 0;
                        foreach (BreakPoint bp in debugger.BreakPoints)
                        {
                            Console.WriteLine("{0} => {1}:{2} {3}", bpcount++, bp.Line, bp.Char, bp.Condition);
                        }
                    }
                    else if (command.StartsWith("dbp "))
                    {
                        // delete break point
                        int bpi = int.Parse(command.Substring(command.IndexOf(" ") + 1));
                        debugger.BreakPoints.RemoveAt(bpi);
                    }
                    else
                    {
                        // try to eval as an immediate
                        Console.WriteLine("{0}", debugger.Immediate(command));
                    }
                }
            }
            finally
            {
                Console.ResetColor();
            }
        }

        static void Main(string[] args)
        {
            // first arg is .js file
            string filename = args[0];
            bool runAgain = true;
            int runIterations = 0;
            JdbEngine debugger = new JdbEngine(filename);

            debugger.OnStart = program =>
                {
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.BackgroundColor = ConsoleColor.Black;
                    Console.WriteLine("Loaded and running {0}", filename);
                    return CommandLoop(program, null, debugger);                    
                };

            debugger.OnStop = result =>
                {
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.BackgroundColor = ConsoleColor.Black;
                    Console.WriteLine("Script finished. Result => {0}", result);
                    CommandLoop(null, null, debugger);
                };

            debugger.OnStep = information =>
                {
                    if (stepping)
                    {
                        return CommandLoop(null, information, debugger);
                    }

                    return true;
                };

            debugger.OnBreak = information =>
                {
                    stepping = true;                    
                    return debugger.OnStep(information);
                };


            while(runAgain)
            {
                runAgain = false;
                var foo = debugger.Run(File.ReadAllText(filename));
                runIterations++;
            }
        }
    }
}
