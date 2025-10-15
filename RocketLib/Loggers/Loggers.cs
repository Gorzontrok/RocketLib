using System;
using RocketLib.UMM;
using UnityModManagerNet;

namespace RocketLib.Loggers
{
    public interface ILogger
    {
        void Log(object message);
        void Log(string message);
        void Error(string message);
        void Exception(Exception exception);
        void Exception(string message, Exception exception);
        void Debug(string message);
        void Warning(string message);
    }

    internal class Logger : ILogger
    {
        public void Log(object message)
        {
            Log(message.ToString());
        }

        public void Log(string message)
        {
            UnityModManager.Logger.Log(message, "[RocketLib] ");
        }

        public void Warning(string message)
        {
            UnityModManager.Logger.Log(message, "[RocketLib] [Warning] ");
        }

        public void Error(string message)
        {
            UnityModManager.Logger.Error(message, "[RocketLib] [Error] ");
        }

        public void Exception(Exception exception)
        {
            Exception(null, exception);
        }
        public void Exception(string message, Exception exception)
        {
            UnityModManager.Logger.LogException(message, exception, "[RocketLib] [Exception] ");
        }

        public void Debug(string message)
        {
            if (Main.settings.ShowDebugLogs)
                UnityModManager.Logger.Log(message, "[RocketLib] [Debug]");
        }
    }
}
