using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Jint;
using Jint.Debugger;
using Jint.Expressions;
using Jint.Native;

namespace LibJdb
{
    public class JdbEngine : JintEngine
    {
        private string m_debugContext = null;
        private Thread m_controlThread = null;
        private object m_sync = new object();
        private AutoResetEvent m_awakeControlEvent = new AutoResetEvent(false);
        private AutoResetEvent m_awakeExecutionEvent = new AutoResetEvent(false);

        private bool m_broke = false;
        private bool Broke 
        { 
            get { lock (m_sync) { return m_broke; } } 
            set { lock (m_sync) { m_broke = value; } }
        }

        private bool m_attached = false;
        private bool Attached
        {
            get { lock (m_sync) { return m_attached; } }
            set { lock (m_sync) { m_attached = value; } }
        }

        private bool m_continue = false;
        private bool DebuggerResult
        {
            get { lock (m_sync) { return m_continue; } }
            set { lock (m_sync) { m_continue = value; } }
        }

        private enum DebuggerEvent
        {
            Start,
            Step,
            Break,
            Stop,
        }

        private Tuple<DebuggerEvent, object> m_currentEvent = null;
        private Tuple<DebuggerEvent, object> CurrentEvent
        {
            get { lock (m_sync) { return m_currentEvent; } }
            set { lock (m_sync) { m_currentEvent = value; } }
        }

        public Func<Program, bool> OnStart;
        public Func<DebugInformation, bool> OnStep;
        public Func<DebugInformation, bool> OnBreak;
        public Action<object> OnStop; 

        public JsScope CurrentScope
        {
            get { return Visitor.CurrentScope; }
        }

        public JdbEngine(string name)
            : base(Options.Ecmascript5 | Options.Strict)
        {
            Init(name);
        }

        public JdbEngine(string name, Options options)
            : base(options)
        {
            Init(name);
        }

        public object Immediate(string expression)
        {
            return Run(expression, true);
        }

        private void Init(string name)
        {
            m_debugContext = name;
            SetDebugMode(true);
            Step = DebugStep;
            Break = Breakpoint;
            Start = RunStart;
            Stop = RunStop;

            SetFunction("dbgprint", new Action<object>(s => { Console.ForegroundColor = ConsoleColor.Gray; Console.Write(s); Console.ResetColor(); }));
            SetFunction("errprint", new Action<object>(s => { Console.ForegroundColor = ConsoleColor.Red; Console.Write(s); Console.ResetColor(); }));
        }

        private void ExecuteWhileBroken(Action action)
        {
            if (Broke)
                return;

            try
            {
                Broke = true;
                action();
            }
            finally
            {
                Broke = false;
            }
        }

        private T ExecuteWhileBroken<T>(Func<T> func, T ifBroken)
        {
            T result = ifBroken;
            ExecuteWhileBroken(() => result = func());
            return result;
        }

        private bool SignalControlAndWait(DebuggerEvent evt, object args)
        {
            CurrentEvent = new Tuple<DebuggerEvent, object>(evt, args);
            m_awakeControlEvent.Set();
            return m_awakeExecutionEvent.WaitOne();
        }

        private bool RunStart(JintEngine me, Program program)
        {
            return ExecuteWhileBroken<bool>(() =>
                {
                    lock (m_sync)
                    {
                        if (m_controlThread != null)
                            return true;

                        Attached = true;
                        m_controlThread = new Thread(ControlThread);
                        //m_controlThread.IsBackground = true;
                        m_controlThread.Start();
                    }
                    
                    // Now we hang out, execution does not continue until we are awoken,
                    //  and then we return Continue
                    bool signaled = SignalControlAndWait(DebuggerEvent.Start, program);

                    if (signaled && DebuggerResult)
                        return true;

                    // Stop our thread
                    Attached = false;
                    SignalControlAndWait(DebuggerEvent.Stop, null);
                    m_controlThread.Join();
                    m_controlThread = null;
                    return false;                    
                }, 
                true);
        }

        private void RunStop(JintEngine me, Program program, object result)
        {
            ExecuteWhileBroken(() =>
                {
                    if (Attached)
                    {
                        Attached = false;

                        // Now we hang out, execution does not continue until we are awoken
                        SignalControlAndWait(DebuggerEvent.Stop, result);
                        m_controlThread.Join();
                        m_controlThread = null;
                    }
                });
        }

        private bool DebugStep(JintEngine me, DebugInformation debugInfo)
        {
            return ExecuteWhileBroken<bool>(() =>
                {
                    SignalControlAndWait(DebuggerEvent.Step, debugInfo);
                    return DebuggerResult;
                }, 
                true);
        }

        private bool Breakpoint(JintEngine me, DebugInformation debugInfo)
        {
            return ExecuteWhileBroken<bool>(() =>
                {
                    SignalControlAndWait(DebuggerEvent.Break, debugInfo);
                    return DebuggerResult;
                },
                true);
        }

        private void ControlThread()
        {
            while (Attached)
            {
                m_awakeControlEvent.WaitOne();

                try
                {
                    DebuggerResult = true;
                    switch (CurrentEvent.Item1)
                    {
                        case DebuggerEvent.Start:
                            if (OnStart != null)
                                DebuggerResult = OnStart(CurrentEvent.Item2 as Program);
                            break;
                        case DebuggerEvent.Stop:
                            if (OnStop != null)
                                OnStop(CurrentEvent.Item2);
                            break;
                        case DebuggerEvent.Break:
                            if (OnBreak != null)
                                DebuggerResult = OnBreak(CurrentEvent.Item2 as DebugInformation);
                            break;
                        case DebuggerEvent.Step:
                            if (OnStep != null)
                                DebuggerResult = OnStep(CurrentEvent.Item2 as DebugInformation);
                            break;
                    }
                }
                finally
                {
                    m_awakeExecutionEvent.Set();
                }
            }
        }
    }
}
