﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace BlazorWorker.Core
{
    public class WorkerInitOptions
    {
        public WorkerInitOptions()
        {
            DependentAssemblyFilenames = new string[] { };
            DependentAssemblyCustomPathMap = new Dictionary<string, string>();
        }

        public string[] DependentAssemblyFilenames { get; set; }
        public Dictionary<string, string> DependentAssemblyCustomPathMap { get; set; }
        public string MessageEndPoint { get; set; }
        public string InitEndPoint { get; set; }
        public string CallbackMethod { get; internal set; }

        public WorkerInitOptions MergeWith(WorkerInitOptions initOptions)
        {
            var redirects = new Dictionary<string, string>(this.DependentAssemblyCustomPathMap);
            foreach (var item in initOptions.DependentAssemblyCustomPathMap)
            {
                redirects[item.Key] = item.Value;
            }

            return new WorkerInitOptions
            {
                DependentAssemblyFilenames = this.DependentAssemblyFilenames
                    .Concat(initOptions.DependentAssemblyFilenames)
                    .Distinct()
                    .ToArray(),
                DependentAssemblyCustomPathMap = redirects,
                CallbackMethod = initOptions.CallbackMethod ?? this.CallbackMethod,
                MessageEndPoint = initOptions.MessageEndPoint ?? this.MessageEndPoint,
                InitEndPoint = initOptions.InitEndPoint ?? this.InitEndPoint,
            };
        }
    }
}