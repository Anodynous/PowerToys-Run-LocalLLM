// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using ManagedCommon;
using Microsoft.PowerToys.Settings.UI.Library;
using Wox.Plugin;
using Clipboard = System.Windows.Clipboard;

namespace Community.PowerToys.Run.Plugin.LocalLLM
{
    public class Main : IPlugin, IDelayedExecutionPlugin, ISettingProvider, IContextMenu
    {
        public static string PluginID => "1CDE310EA37046F797117442344A72F2";

        public string Name => "LocalLLM";

        public string Description => "Uses Local LLM to output answer";

        private static readonly HttpClient Client = new();

        private string IconPath { get; set; }

        private PluginInitContext Context { get; set; }

        private string endpoint;
        private string model;

        public IEnumerable<PluginAdditionalOption> AdditionalOptions =>
        [
            new()
            {
                Key = "LLMEndpoint",
                DisplayLabel = "LLM Endpoint",
                DisplayDescription = "Enter the endpoint of your LLM model. Ex. http://localhost:11434/api/generate",
                PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
                TextValue = "http://localhost:11434/api/generate",
            },
            new()
            {
                Key = "Model",
                DisplayLabel = "Model",
                DisplayDescription = "Enter the Model to be used in Ollama. Ex. llama3.1(default). Make sure to pull model in ollama before using here.",
                PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
                TextValue = "llama3.1",
            },
        ];

        public void UpdateSettings(PowerLauncherPluginSettings settings)
        {
            if (settings != null && settings.AdditionalOptions != null)
            {
                endpoint = settings.AdditionalOptions.FirstOrDefault(x => x.Key == "LLMEndpoint")?.TextValue ?? "http://localhost:11434/api/generate";
                model = settings.AdditionalOptions.FirstOrDefault(x => x.Key == "Model")?.TextValue ?? "llama3.1";
            }
        }

        public void Init(PluginInitContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Context.API.ThemeChanged += OnThemeChanged;
            UpdateIconPath(Context.API.GetCurrentTheme());
        }

        private void OnThemeChanged(Theme currentTheme, Theme newTheme) => UpdateIconPath(newTheme);

        private void UpdateIconPath(Theme theme) => IconPath = theme == Theme.Light || theme == Theme.HighContrastWhite ? Context?.CurrentPluginMetadata.IcoPathLight : Context?.CurrentPluginMetadata.IcoPathDark;

        public List<Result> Query(Query query, bool delayedExecution)
        {
            var input = query.Search;
            var response = QueryLLMStreamAsync(input).Result;

            return
            [
                new()
                {
                    Title = "LLM Response",
                    SubTitle = response,
                    IcoPath = "Images\\icon.png",
                    Action = e =>
                    {
                        Context.API.ShowMsg("LLM Response", response);
                        return true;
                    },
                    ContextData = new Dictionary<string, string> { { "copy", response } },
                },
            ];
        }

        public async Task<string> QueryLLMStreamAsync(string input)
        {
            var endpointUrl = endpoint;
            var requestBody = new
            {
                model,
                prompt = input,
                stream = true,
            };

            try
            {
                var response = await Client.PostAsync(endpointUrl, new StringContent(
                    JsonSerializer.Serialize(requestBody),
                    System.Text.Encoding.UTF8,
                    "application/json"));

                response.EnsureSuccessStatusCode();

                var responseStream = await response.Content.ReadAsStreamAsync();
                using var streamReader = new System.IO.StreamReader(responseStream);
                #nullable enable
                string? line;
                #nullable disable
                string finalResponse = string.Empty;

                while ((line = await streamReader.ReadLineAsync()) != null)
                {
                    var json = JsonSerializer.Deserialize<JsonElement>(line);
                    var part = json.GetProperty("response").GetString();
                    finalResponse += part;
                }

                return finalResponse;
            }
            catch (Exception ex)
            {
                return $"Error querying LLM: {ex.Message}";
            }
        }

        public List<Result> Query(Query query)
        {
            List<Result> results = [];
            return results;
        }

        public Control CreateSettingPanel()
        {
            throw new NotImplementedException();
        }

        public List<ContextMenuResult> LoadContextMenus(Result selectedResult)
        {
            List<ContextMenuResult> results = [];

            if (selectedResult?.ContextData is Dictionary<string, string> contextData)
            {
                if (contextData.TryGetValue("copy", out string value))
                {
                    results.Add(
                        new ContextMenuResult
                        {
                            PluginName = Name,
                            Title = "Copy (Enter)",
                            FontFamily = "Segoe Fluent Icons,Segoe MDL2 Assets",
                            Glyph = "\xE8C8",
                            AcceleratorKey = Key.Enter,
                            Action = _ =>
                            {
                                Clipboard.SetText(value.ToString());
                                return true;
                            },
                        });
                }
            }

            return results;
        }
    }
}
