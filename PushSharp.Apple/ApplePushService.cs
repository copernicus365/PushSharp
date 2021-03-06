﻿using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PushSharp.Core;

namespace PushSharp.Apple
{
	public class ApplePushService : PushServiceBase
	{
		FeedbackService feedbackService;
		CancellationTokenSource cancelTokenSource;
		Timer timerFeedback = null;

		public static bool Default_RunFeedbackServicePriorToDispose { get; set; }

		/// <summary>
		/// On call to Dispose, will first call RunFeedbackService when true.
		/// </summary>
		public bool RunFeedbackServicePriorToDispose { get; set; }

		#region Constructor Indirections

		public ApplePushService(ApplePushChannelSettings channelSettings)
			: this(default(IPushChannelFactory), channelSettings, default(IPushServiceSettings))
		{
		}

		public ApplePushService(ApplePushChannelSettings channelSettings, IPushServiceSettings serviceSettings)
			: this(default(IPushChannelFactory), channelSettings, serviceSettings)
		{
		}

		public ApplePushService(IPushChannelFactory pushChannelFactory, ApplePushChannelSettings channelSettings)
			: this(pushChannelFactory, channelSettings, default(IPushServiceSettings))
		{
		}

		#endregion

		public ApplePushService(IPushChannelFactory pushChannelFactory, ApplePushChannelSettings channelSettings, IPushServiceSettings serviceSettings)
			: base(pushChannelFactory ?? new ApplePushChannelFactory(), channelSettings, serviceSettings)
		{
			RunFeedbackServicePriorToDispose = Default_RunFeedbackServicePriorToDispose;
			var appleChannelSettings = channelSettings;
			cancelTokenSource = new CancellationTokenSource();

			//allow control over feedback call interval, if set to zero, don't make feedback calls automatically
			if (appleChannelSettings.FeedbackIntervalMinutes > 0)
			{
				feedbackService = new FeedbackService();
				feedbackService.OnFeedbackReceived += feedbackService_OnFeedbackReceived;
				feedbackService.OnFeedbackException += (Exception ex) => this.RaiseServiceException (ex);

				if (timerFeedback == null)
				{
					timerFeedback = new Timer(new TimerCallback((state) =>
					{
						RunFeedbackService();

						//Timer will run first after 10 seconds, then every 10 minutes to get feedback!
					}), null, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(appleChannelSettings.FeedbackIntervalMinutes));
				}
			}

			//Apple has documented that they only want us to use 20 connections to them
			base.ServiceSettings.MaxAutoScaleChannels = 20;
		}

		/// <summary>
		/// Runs the FeedbackService. Removed to this public method so that users with an ApplePushService 
		/// reference can directly call it when desired (originally was embedded in the timerFeedback callback).
		/// </summary>
		public void RunFeedbackService()
		{
			try {
				feedbackService.Run(base.ChannelSettings as ApplePushChannelSettings, this.cancelTokenSource.Token);
			} catch (Exception ex) {
				base.RaiseServiceException(ex);
			}
		}

		void feedbackService_OnFeedbackReceived(string deviceToken, DateTime timestamp)
		{
			base.RaiseSubscriptionExpired(deviceToken, timestamp.ToUniversalTime(), null);
		}

		public override bool BlockOnMessageResult
		{
			get { return false; }
		}

		/// <summary>
		/// Overrides PushServiceBase.Dispose, allow us to first call RunFeedbackService
		/// if RunFeedbackServicePriorToDispose is true, then calls base.Dispose.
		/// </summary>
		public new void Dispose()
		{
			if(this.RunFeedbackServicePriorToDispose) // is FALSE by default
				RunFeedbackService(); // has try/catch, so on ex should still reach base.Dispose()
			base.Dispose();
		}

	}

	public class ApplePushChannelFactory : IPushChannelFactory
	{
		public IPushChannel CreateChannel(IPushChannelSettings channelSettings)
		{
			if (!(channelSettings is ApplePushChannelSettings))
				throw new ArgumentException("Channel Settings must be of type ApplePushChannelSettings");

			return new ApplePushChannel(channelSettings as ApplePushChannelSettings);
		}
	}
}
