﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Metrics.Logging;
using Metrics.MetricData;
using Metrics.Reports;

namespace Metrics
{
    public sealed class MetricsConfig: Utils.IHideObjectMembers
    {
        private static readonly ILog log = LogProvider.GetCurrentClassLogger();

        public static readonly bool GloballyDisabledMetrics = ReadGloballyDisableMetricsSetting();

        private readonly MetricsContext context;
        private readonly MetricsReports reports;

        private Func<HealthStatus> healthStatus;        

        private SamplingType defaultSamplingType = SamplingType.ExponentiallyDecaying;

        private bool isDisabled = MetricsConfig.GloballyDisabledMetrics;

        /// <summary>
        /// Gets the currently configured default sampling type to use for histogram sampling.
        /// </summary>
        public SamplingType DefaultSamplingType
        {
            get
            {
                Debug.Assert(this.defaultSamplingType != SamplingType.Default);
                return this.defaultSamplingType;
            }
        }

        public MetricsConfig(MetricsContext context)
        {
            this.context = context;

            if (!GloballyDisabledMetrics)
            {
                this.healthStatus = HealthChecks.GetStatus;
                this.reports = new MetricsReports(this.context.DataProvider, this.healthStatus);

                this.context.Advanced.ContextDisabled += (s, e) =>
                {
                    this.isDisabled = true;
                    DisableAllReports();
                };
            }
        }
       
        /// <summary>
        /// Configure Metrics library to use a custom health status reporter. By default HealthChecks.GetStatus() is used.
        /// </summary>
        /// <param name="healthStatus">Function that provides the current health status.</param>
        /// <returns>Chain-able configuration object.</returns>
        public MetricsConfig WithHealthStatus(Func<HealthStatus> healthStatus)
        {
            if (!this.isDisabled)
            {
                this.healthStatus = healthStatus;
            }
            return this;
        }

        /// <summary>
        /// Error handler for the metrics library. If a handler is registered any error will be passed to the handler.
        /// By default unhandled errors are logged, printed to console if Environment.UserInteractive is true, and logged with Trace.TracError.
        /// </summary>
        /// <param name="errorHandler">Action with will be executed with the exception.</param>
        /// <param name="clearExistingHandlers">Is set to true, remove any existing handler.</param>
        /// <returns>Chain able configuration object.</returns>
        public MetricsConfig WithErrorHandler(Action<Exception> errorHandler, bool clearExistingHandlers = false)
        {
            if (clearExistingHandlers)
            {
                MetricsErrorHandler.Handler.ClearHandlers();
            }

            if (!this.isDisabled)
            {
                MetricsErrorHandler.Handler.AddHandler(errorHandler);
            }

            return this;
        }

        /// <summary>
        /// Error handler for the metrics library. If a handler is registered any error will be passed to the handler.
        /// By default unhandled errors are logged, printed to console if Environment.UserInteractive is true, and logged with Trace.TracError.
        /// </summary>
        /// <param name="errorHandler">Action with will be executed with the exception and a specific message.</param>
        /// <param name="clearExistingHandlers">Is set to true, remove any existing handler.</param>
        /// <returns>Chain able configuration object.</returns>
        public MetricsConfig WithErrorHandler(Action<Exception, string> errorHandler, bool clearExistingHandlers = false)
        {
            if (clearExistingHandlers)
            {
                MetricsErrorHandler.Handler.ClearHandlers();
            }

            if (!this.isDisabled)
            {
                MetricsErrorHandler.Handler.AddHandler(errorHandler);
            }

            return this;
        }

        /// <summary>
        /// Configure the way metrics are reported
        /// </summary>
        /// <param name="reportsConfig">Reports configuration action</param>
        /// <returns>Chain-able configuration object.</returns>
        public MetricsConfig WithReporting(Action<MetricsReports> reportsConfig)
        {
            if (!this.isDisabled)
            {
                reportsConfig(this.reports);
            }

            return this;
        }

        /// <summary>
        /// This method is used for customizing the metrics configuration.
        /// The <paramref name="extension"/> will be called with the current MetricsContext and HealthStatus provider.
        /// </summary>
        /// <remarks>
        /// In general you don't need to call this method directly.
        /// </remarks>
        /// <param name="extension">Action to apply extra configuration.</param>
        /// <returns>Chain-able configuration object.</returns>
        public MetricsConfig WithConfigExtension(Action<MetricsContext, Func<HealthStatus>> extension)
        {
            if (this.isDisabled)
            {
                return this;
            }

            return WithConfigExtension((m, h) => { extension(m, h); return this; }, () => this);
        }

        /// <summary>
        /// This method is used for customizing the metrics configuration.
        /// The <paramref name="extension"/> will be called with the current MetricsContext and HealthStatus provider.
        /// </summary>
        /// <remarks>
        /// In general you don't need to call this method directly.
        /// </remarks>
        /// <param name="extension">Action to apply extra configuration.</param>
        /// <returns>The result of calling the extension.</returns>
        [Obsolete("This configuration method ignores the CompletelyDisableMetrics setting. Please use the overload instead.")]
        public T WithConfigExtension<T>(Func<MetricsContext, Func<HealthStatus>, T> extension)
        {
            return extension(this.context, this.healthStatus);
        }

        /// <summary>
        /// This method is used for customizing the metrics configuration.
        /// The <paramref name="extension"/> will be called with the current MetricsContext and HealthStatus provider.
        /// </summary>
        /// <remarks>
        /// In general you don't need to call this method directly.
        /// </remarks>
        /// <param name="extension">Action to apply extra configuration.</param>
        /// <param name="defaultValueProvider">Default value provider for T, which will be used when metrics are disabled.</param>
        /// <returns>The result of calling the extension.</returns>
        public T WithConfigExtension<T>(Func<MetricsContext, Func<HealthStatus>, T> extension, Func<T> defaultValueProvider)
        {
            if (this.isDisabled)
            {
                return defaultValueProvider();
            }

            return extension(this.context, this.healthStatus);
        }

        /// <summary>
        /// Configure the default sampling type to use for histograms.
        /// </summary>
        /// <param name="type">Type of sampling to use.</param>
        /// <returns>Chain-able configuration object.</returns>
        public MetricsConfig WithDefaultSamplingType(SamplingType type)
        {
            if (this.isDisabled)
            {
                return this;
            }

            if (type == SamplingType.Default)
            {
                throw new ArgumentException("Sampling type other than default must be specified", nameof(type));
            }
            this.defaultSamplingType = type;
            return this;
        }

        public MetricsConfig WithInternalMetrics()
        {
            if (this.isDisabled)
            {
                return this;
            }

            Metric.EnableInternalMetrics();
            return this;
        }
  

        private void DisableAllReports()
        {
            this.reports.StopAndClearAllReports();
        }

        internal void ApplySettingsFromConfigFile()
        {
            if (!GloballyDisabledMetrics)
            {
                ConfigureCsvReports();
            }
        }

        private void ConfigureCsvReports()
        {
            try
            {
                var csvMetricsPath = ConfigurationManager.AppSettings["Metrics.CSV.Path"];
                var csvMetricsInterval = ConfigurationManager.AppSettings["Metrics.CSV.Interval.Seconds"];

                if (!string.IsNullOrEmpty(csvMetricsPath) && !string.IsNullOrEmpty(csvMetricsInterval))
                {
                    int seconds;
                    if (int.TryParse(csvMetricsInterval, out seconds) && seconds > 0)
                    {
                        WithReporting(r => r.WithCSVReports(csvMetricsPath, TimeSpan.FromSeconds(seconds)));
                        log.Debug($"Metrics: Storing CSV reports in {csvMetricsPath} every {seconds} seconds.");
                    }
                }
            }
            catch (Exception x)
            {
                MetricsErrorHandler.Handle(x, "Invalid Metrics Configuration: Metrics.CSV.Path must be a valid path and Metrics.CSV.Interval.Seconds must be an integer > 0 ");
            }
        }

        private static bool ReadGloballyDisableMetricsSetting()
        {
            try
            {
                var isDisabled = ConfigurationManager.AppSettings["Metrics.CompletelyDisableMetrics"];
                return !string.IsNullOrEmpty(isDisabled) && isDisabled.Equals("TRUE", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception x)
            {
                MetricsErrorHandler.Handle(x, "Invalid Metrics Configuration: Metrics.CompletelyDisableMetrics must be set to true or false");
                return false;
            }
        }
    }
}
