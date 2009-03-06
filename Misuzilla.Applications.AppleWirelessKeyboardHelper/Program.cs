using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using IronPython.Runtime.Types;
using Microsoft.Scripting;
using Microsoft.Scripting.Actions;
using Microsoft.Scripting.Hosting;

namespace Misuzilla.Applications.AppleWirelessKeyboardHelper
{
    public class Program
    {
        private static NotifyIcon _notifyIcon;
        private const String ApplicationName = "Apple Wireless Keyboard Helper";
        private static ScriptRuntime _scriptRuntime;
        private static ScriptScope _scriptScope;

        public static Int32 BalloonTipTimeout = 1500;

        private const UInt32 JISAlphaNumericKeyScanCode = 113; // 113
        private const UInt32 JISKanaKeyScanCode = 114; // 114
        
        //[STAThread]
        static void Main()
        {
            using (Helper helper = new Helper())
            {
                helper.FnKeyCombinationDown += delegate(Object sender, AppleKeyboardEventArgs e)
                {
                    StringBuilder funcName = new StringBuilder("OnDown");
                    if (e.AppleKeyState == AppleKeyboardKeys.Fn)
                        funcName.Append("_Fn");
                    if (e.AppleKeyState == AppleKeyboardKeys.Eject)
                        funcName.Append("_Eject");

                    funcName.Append("_").Append(e.Key.ToString());

                    Call(funcName.ToString(), e);

                    e.Handled = true;
                };

                helper.KeyUp += delegate(Object sender, AppleKeyboardEventArgs e)
                {
                    if (e.KeyEventStruct.wScan != JISAlphaNumericKeyScanCode && e.KeyEventStruct.wScan != JISKanaKeyScanCode)
                        return;
                    
                    StringBuilder funcName = new StringBuilder("OnUp");
                    if (e.AppleKeyState == AppleKeyboardKeys.Fn)
                        funcName.Append("_Fn");
                    if (e.AppleKeyState == AppleKeyboardKeys.Eject)
                        funcName.Append("_Eject");

                    funcName.Append("_").Append((e.KeyEventStruct.wScan == JISAlphaNumericKeyScanCode) ? "JISAlphaNumeric" : "JISKana");

                    Call(funcName.ToString(), e);

                    e.Handled = true;
                };

                helper.SpecialKeyDown += delegate(Object sender, KeyEventArgs e)
                {
                    StringBuilder funcName = new StringBuilder("OnDown");
                    if (e.AppleKeyboardKey == AppleKeyboardKeys.Fn)
                        funcName.Append("_Fn");
                    if (e.AppleKeyboardKey == AppleKeyboardKeys.Eject)
                        funcName.Append("_Eject");
                    if (e.IsPowerButtonDown)
                        funcName.Append("_Power");

                    Call(funcName.ToString(), e);
                };

                helper.Disconnected += delegate
                {
                    ShowBalloonTip(Resources.Strings.KeyboardDisconnected, ToolTipIcon.Warning);
                    helper.Shutdown();
                    while (!helper.Start())
                    {
                        // retry at interval of 10sec
                        System.Threading.Thread.Sleep(10000);
                    }
                    ShowBalloonTip(Resources.Strings.KeyboardConnected, ToolTipIcon.Info);
                };

                if (!helper.Start())
                {
                    MessageBox.Show(Resources.Strings.KeyboardNotConnected, ApplicationName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                helper.Hook();

                SetupNotifyIcon();
                LoadScripts();

                Application.Run();

                _notifyIcon.Visible = false;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="funcName"></param>
        /// <param name="e"></param>
        private static void Call(String funcName, EventArgs e)
        {
            Object funcObj;
            if (!_scriptScope.TryGetVariable(funcName, out funcObj))
                return;

            try
            {
                _scriptScope.Engine.Operations.Call(funcObj, null);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), ApplicationName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }


        
        /// <summary>
        /// 
        /// </summary>
        private static void LoadScripts()
        {
            // 古いものをすべて削除
            OnUnload(EventArgs.Empty);
            Unload = null;
            Load = null;
            
            if (_scriptRuntime != null)
                _scriptRuntime.Shutdown();

#pragma warning disable 0618
            ScriptRuntimeSetup scriptRuntimeSetup = new ScriptRuntimeSetup();
            scriptRuntimeSetup.LanguageSetups.Add(new LanguageSetup("IronPython.Runtime.PythonContext, IronPython", "IronPython 2.0", new[] { "IronPython", "Python", "py" }, new[] { ".py" }));
            _scriptRuntime = new ScriptRuntime(scriptRuntimeSetup);
            _scriptRuntime.LoadAssembly(Assembly.GetExecutingAssembly());
            _scriptRuntime.LoadAssembly(Assembly.LoadWithPartialName("mscorlib"));
            _scriptRuntime.LoadAssembly(Assembly.LoadWithPartialName("System"));
            _scriptRuntime.LoadAssembly(Assembly.LoadWithPartialName("System.Windows.Forms"));
#pragma warning restore 0618

            _scriptScope = _scriptRuntime.CreateScope();

            Boolean hasScripts = false;
            if (Directory.Exists("Scripts"))
            {
                foreach (String path in Directory.GetFiles("Scripts", "*.py"))
                {
                    Debug.WriteLine("Load Script: " + path);
                    try
                    {
                        ScriptEngine engine = _scriptRuntime.GetEngineByFileExtension(Path.GetExtension(path));
                        engine.ExecuteFile(path, _scriptScope);
                        hasScripts = true;
                    }
                    catch (SyntaxErrorException se)
                    {
                        MessageBox.Show(String.Format(Resources.Strings.ScriptSyntaxException, path, se.Line, se.Column, se.Message), ApplicationName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    catch (Exception e)
                    {
                        MessageBox.Show(String.Format(Resources.Strings.ScriptException, path, e.Message), ApplicationName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }

            // 一つも読み込んでいなかったらデフォルト
            if (!hasScripts)
            {
                ScriptSource scriptSource = _scriptRuntime.GetEngine("py").CreateScriptSourceFromString(Resources.Strings.DefaultPythonScript, SourceCodeKind.Statements);
                scriptSource.Execute(_scriptScope);
            }

            OnLoad(EventArgs.Empty);
            
            ShowBalloonTip(Resources.Strings.ScriptsLoaded, ToolTipIcon.Info);
        }

        /// <summary>
        /// 
        /// </summary>
        private static void SetupNotifyIcon()
        {
            _notifyIcon = new NotifyIcon();
            _notifyIcon.Icon = Resources.Common.AppleWirelessKeyboardHelperTrayIcon16x16;
            _notifyIcon.Text = ApplicationName;
            _notifyIcon.Visible = true;
            _notifyIcon.ContextMenu = new ContextMenu(new MenuItem[]{
                new MenuItem(Resources.Strings.MenuItemReloadScripts, delegate {
                    LoadScripts();
                })
                , new MenuItem("-")
                , new MenuItem(Resources.Strings.MenuItemExit, delegate {
                    Application.Exit();
                })
            });
        }

        public static void ShowBalloonTip(String text)
        {
            ShowBalloonTip(text, ToolTipIcon.Info);
        }
        
        public static void ShowBalloonTip(String text, ToolTipIcon toolTipIcon)
        {
            _notifyIcon.ShowBalloonTip(BalloonTipTimeout, ApplicationName, text, toolTipIcon);
        }

        public static event EventHandler Unload;
        private static void OnUnload(EventArgs e)
        {
            try
            {
                if (Unload != null)
                    Unload(new Object(), e);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }
        }
        public static event EventHandler Load;
        private static void OnLoad(EventArgs e)
        {
            try
            {
                if (Load != null)
                    Load(new Object(), e);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }
        }
    }
}
