using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace LightGateway.Command
{
    public class Base
    {
        public delegate bool Func<T, Boolean>(T t);//定义委托类型

        public class BaseNotify : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler PropertyChanged;

            /// <summary>
            /// 属性发生改变时调用该方法发出通知
            /// </summary>
            /// <param name="propertyName">属性名称</param>
            public void RaisePropertyChanged(string propertyName)
            {
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
                }
            }

            protected virtual void SetAndNotifyIfChanged<T>(string propertyName, ref T oldValue, T newValue)
            {
                if (oldValue == null && newValue == null) return;
                if (oldValue != null && oldValue.Equals(newValue)) return;
                if (newValue != null && newValue.Equals(oldValue)) return;
                oldValue = newValue;
                RaisePropertyChanged(propertyName);
            }

            protected bool SetProperty<T>(ref T target, T value, [CallerMemberName] string propertyName = null)
            {
                if (EqualityComparer<T>.Default.Equals(target, value))
                {
                    return false;
                }

                target = value;
                RaisePropertyChanged(propertyName);
                return true;
            }
        }


        public class BaseCommand<T> : ICommand where T : class
        {
            #region 字段
            readonly Func<T, Boolean> _canExecute;
            readonly Action<T> _execute;
            #endregion

            #region 构造函数
            public BaseCommand(Action<T> execute)
                : this(execute, null)
            {
            }

            public BaseCommand(Action<T> execute, Func<T, Boolean> canExecute)
            {
                if (execute == null)
                    throw new ArgumentNullException("execute");
                _execute = execute;
                _canExecute = canExecute;
            }
            #endregion

            #region ICommand的成员
            public event EventHandler CanExecuteChanged
            {
                add
                {

                    if (_canExecute != null)
                        CommandManager.RequerySuggested += value;
                }
                remove
                {

                    if (_canExecute != null)
                        CommandManager.RequerySuggested -= value;
                }
            }

            [DebuggerStepThrough]
            public Boolean CanExecute(Object parameter)
            {
                return _canExecute == null ? true : _canExecute((T)parameter);
            }

            public void Execute(Object parameter)
            {
                _execute(parameter as T);
            }
            #endregion
        }
    }
}