﻿using System;
using System.Collections.Generic;
using System.IO;
 using System.Linq;
 using System.Net;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Net;
using Android.OS;
using Android.Support.V7.App;
using Android.Telephony;
 using Android.Widget;
 using ISimpleHttpListener.Rx.Enum;
using ISimpleHttpListener.Rx.Model;
using Java.Lang;
using Java.Lang.Reflect;
using Plugin.Connectivity;
using SimpleHttpListener.Rx.Extension;
using SimpleHttpListener.Rx.Model;
using SimpleHttpListener.Rx.Service;
using Boolean = Java.Lang.Boolean;
using Exception = System.Exception;
using Uri = System.Uri;

namespace MobileNetworkManager
{
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme", MainLauncher = true)]
    public class MainActivity : AppCompatActivity
    {
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            // Set our view from the "main" layout resource
            SetContentView(Resource.Layout.activity_main);

            var edittext = FindViewById<TextView>(Resource.Id.textView1);

            var ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var address in ipHostInfo.AddressList)
            {
                edittext.Text = edittext.Text + "\n" + address;
            }

            var tcpListener = new TcpListener(IPAddress.Parse("0.0.0.0"), 8000)
            {
                ExclusiveAddressUse = false,
            };

            var httpSender = new HttpSender();

            var cts = new CancellationTokenSource();

            var disposable = tcpListener
                .ToHttpListenerObservable(cts.Token)
                .Do(r =>
                {
                    Console.WriteLine($"Remote Address: {r.RemoteIpEndPoint.Address}");
                    Console.WriteLine($"Remote Port: {r.RemoteIpEndPoint.Port}");
                    Console.WriteLine("--------------***-------------");
                })
                // Send reply to browser
                .Select(r => Observable.FromAsync(() => SendResponseAsync(r, httpSender)))
                .Concat()
                .Subscribe(r => { Console.WriteLine("Reply sent."); },
                    ex => { Console.WriteLine($"Exception: {ex}"); },
                    () => { Console.WriteLine("Completed."); });
        }

        async Task SendResponseAsync(IHttpRequestResponse request, HttpSender httpSender)
        {
            if (request.RequestType == RequestType.TCP)
            {
                SetMobileDataEnabled(false);

                WaitWhile(async () => await TestConnectivity(), 2000, 60000).Wait();

                SetMobileDataEnabled(true);

                WaitWhile(async () => !await TestConnectivity(), 2000, 60000).Wait();

                var response = new HttpResponse
                {
                    StatusCode = (int) HttpStatusCode.OK,
                    ResponseReason = HttpStatusCode.OK.ToString(),
                    Headers = new Dictionary<string, string>
                    {
                        {"Date", DateTime.UtcNow.ToString("r")},
                        {"Content-Type", "text/html; charset=UTF-8"},
                    },
                    Body = new MemoryStream(Encoding.UTF8.GetBytes($"OK"))
                };

                await httpSender.SendTcpResponseAsync(request, response).ConfigureAwait(false);
            }
        }

        public static async Task WaitWhile(Func<Task<bool>> condition, int frequency = 25, int timeout = -1)
        {
            var waitTask = Task.Run(async () =>
            {
                while (await condition()) await Task.Delay(frequency);
            });

            if (waitTask != await Task.WhenAny(waitTask, Task.Delay(timeout)))
                throw new TimeoutException();
        }

        private async Task<bool> TestConnectivity()
        {
            return await CrossConnectivity.Current.IsRemoteReachable("google.com");
        }

        void SetMobileDataEnabled(bool enabled)
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Lollipop)
            {
                Console.WriteLine("Device does not support mobile data toggling.");
                return;
            }

            try
            {
                if (Build.VERSION.SdkInt <= BuildVersionCodes.KitkatWatch
                    && Build.VERSION.SdkInt >= BuildVersionCodes.Gingerbread)
                {
                    var conman =
                        (ConnectivityManager) GetSystemService(ConnectivityService);
                    var conmanClass = Class.ForName(conman.Class.Name);
                    var iConnectivityManagerField = conmanClass.GetDeclaredField("mService");
                    iConnectivityManagerField.Accessible = true;
                    var iConnectivityManager = iConnectivityManagerField.Get(conman);
                    var iConnectivityManagerClass =
                        Class.ForName(iConnectivityManager.Class.Name);
                    var setMobileDataEnabledMethod =
                        iConnectivityManagerClass.GetDeclaredMethod("setMobileDataEnabled", Boolean.Type);
                    setMobileDataEnabledMethod.Accessible = true;

                    setMobileDataEnabledMethod.Invoke(iConnectivityManager, enabled);
                }

                if (Build.VERSION.SdkInt < BuildVersionCodes.Gingerbread)
                {
                    var tm = (TelephonyManager) GetSystemService(TelephonyService);

                    var telephonyClass = Class.ForName(tm.Class.Name);
                    var getITelephonyMethod = telephonyClass.GetDeclaredMethod("getITelephony");
                    getITelephonyMethod.Accessible = true;

                    var stub = getITelephonyMethod.Invoke(tm);
                    var iTelephonyClass = Class.ForName(stub.Class.Name);

                    Method dataConnSwitchMethod = null;
                    dataConnSwitchMethod =
                        iTelephonyClass.GetDeclaredMethod(
                            enabled ? "disableDataConnectivity" : "enableDataConnectivity");

                    dataConnSwitchMethod.Accessible = true;
                    dataConnSwitchMethod.Invoke(stub);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Device does not support mobile data toggling.");
            }
        }
    }
}