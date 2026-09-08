using log4net;
using log4net.Appender;
using log4net.Core;
using log4net.Filter;
using log4net.Layout;
using log4net.Repository;
using log4net.Repository.Hierarchy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace RTC.Services
{
    /// <summary>
    /// 日志服务类
    /// </summary>
    public class LogService:IDisposable
    {
        private ILoggerRepository repository;

        public ILog Default { get; }

        public LogService(string name="",string dir="")
        {
            repository = LogManager.CreateRepository(name);

            //配置输出日志格式。%m表示message即日志信息。%n表示newline换行
            PatternLayout layout = new PatternLayout(@"%d %-5p - %m%n");
            layout.ActivateOptions();


            //配置日志级别为所有级别
            LevelMatchFilter filter = new LevelMatchFilter();
            filter.LevelToMatch = Level.All;
            filter.ActivateOptions();

            //文件
            RollingFileAppender fileAppender = new RollingFileAppender();
            fileAppender.File = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"log",dir,name,"default.log");
            fileAppender.ImmediateFlush = true;
            fileAppender.MaxSizeRollBackups = 20;
            fileAppender.MaximumFileSize = "100MB";
            fileAppender.RollingStyle = RollingFileAppender.RollingMode.Size;
            fileAppender.StaticLogFileName = false;
            fileAppender.LockingModel = new FileAppender.MinimalLock();
            fileAppender.AddFilter(filter);
            fileAppender.Layout = layout;
            fileAppender.AppendToFile = true;
            fileAppender.ActivateOptions();

            //控制台
            ConsoleAppender consoleAppender = new ConsoleAppender();
            consoleAppender.Layout = layout;
            consoleAppender.ActivateOptions();

            log4net.Config.BasicConfigurator.Configure(repository, fileAppender, consoleAppender);
            Default = LogManager.GetLogger(repository.Name, "default");
            
        }

        public void Dispose()
        {
            repository.Shutdown();
        }
    }
}
