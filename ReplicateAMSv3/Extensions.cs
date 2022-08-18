using Microsoft.Rest.Azure.Authentication;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ReplicateAMSv3
{
    public static class Helpers
    {
        private static string logFile;

        public static void WriteLine(string message, int level)
        {
            string msg = FormatTraceMessage(message, level);
            Console.WriteLine(msg);

            try
            {
                File.AppendAllText(logFile, msg + Environment.NewLine);
            }
            catch (Exception ex)
            {
                //do nothing
            }
        }

        public static void Write(string message, int level)
        {
            string msg = FormatTraceMessage(message, level);
            Console.Write(msg);

            try
            {
                File.AppendAllText(logFile, msg);
            }
            catch (Exception ex)
            {
                //do nothing
            }
        }

        private static string FormatTraceMessage(string message, int level)
        {
            level = level >= 1 ? level : 1;

            return $"{new string(' ', (level - 1) * 3)}{message}";
        }

        public static string CreateLogFile()
        {
            logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", DateTime.Now.ToString("yyyyMMddHHmmss") + ".txt");

            if (!Directory.Exists(Path.GetDirectoryName(logFile)))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logFile));
            }

            using (File.Create(logFile)) { }

            return logFile;
        }

        public static ActiveDirectoryServiceSettings GetActiveDirectoryServiceSettings(string aadSettings)
        {
            switch (aadSettings.ToLower())
            {
                case "azure":
                    return ActiveDirectoryServiceSettings.Azure;

                case "azurechina":
                    return ActiveDirectoryServiceSettings.AzureChina;

                case "azureusgovernment":
                    return ActiveDirectoryServiceSettings.AzureUSGovernment;

                default:
                    WriteLine($"'{aadSettings}' is unknown Azure Active Directory Settings. Use 'Azure' instead", 1);
                    return ActiveDirectoryServiceSettings.Azure;

            }
        }
    }
}
