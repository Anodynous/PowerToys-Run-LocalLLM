// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using ManagedCommon;
using Microsoft.PowerToys.Settings.UI.Library;
using Wox.Plugin;
using Wox.Plugin.Logger;
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
        private string clipboardTriggerKeyword;
        private string sendTriggerKeyword;
        private List<string> modelNames = ["Failed to fetch"];

        public IEnumerable<PluginAdditionalOption> AdditionalOptions =>
        [

            new()
            {
                Key = "LLMEndpoint",
                DisplayLabel = "LLM Endpoint",
                DisplayDescription = "Enter the endpoint of your LLM model. E.g. http://localhost:11434/api/generate",
                PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
                TextValue = "http://localhost:11434/api/generate",
            },
            new()
            {
                Key = "Model",
                DisplayLabel = "Model",
                DisplayDescription = "Enter the Model to be used in Ollama. E.g. llama3.1(default). Make sure to pull model in ollama before using here.",
                PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Combobox,
                ComboBoxItems = GetOllamaModelList(), // CreateComboBoxItems(modelNames),
                ComboBoxValue = 0,
            },
            new()
            {
                Key = "ClipboardTriggerKeyword",
                DisplayLabel = "Clipboard Trigger Keyword",
                DisplayDescription = "Enter keyword which will be substituted by the clipboard contents in the query.",
                PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
                TextValue = "<clip>", // Default value
            },
            new()
            {
                Key = "SendTriggerKeyword",
                DisplayLabel = "Send Trigger Keyword",
                DisplayDescription = "Enter keyword which will trigger the query to be sent to Ollama.",
                PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
                TextValue = "LL", // Default value
            },
        ];

        private List<KeyValuePair<string, string>> CreateComboBoxItems(List<string> modelNames)
        {
            var comboBoxItems = new List<KeyValuePair<string, string>>();

            for (int i = 0; i < modelNames.Count; i++)
            {
                comboBoxItems.Add(new KeyValuePair<string, string>(modelNames[i], i.ToString(CultureInfo.InvariantCulture)));
            }

            return comboBoxItems;
        }

        private async Task<List<string>> GetModelListAsync()
        {
            var endpointUrl = "http://127.0.0.1:11434/api/tags";

            try
            {
                var response = await Client.GetAsync(endpointUrl);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var jsonDocument = JsonDocument.Parse(content);

                var modelNames = new List<string>();

                foreach (var model in jsonDocument.RootElement.GetProperty("models").EnumerateArray())
                {
                    var modelName = model.GetProperty("name").GetString();
                    if (!string.IsNullOrEmpty(modelName))
                    {
                        modelNames.Add(modelName);
                    }
                }

                Log.Info(string.Join("\n", modelNames), typeof(Task));
                return modelNames;
            }
            catch (Exception ex)
            {
                Log.Error($"Error fetching models: {ex.Message}", typeof(Task));
                return new List<string>();
            }
        }

        private static List<KeyValuePair<string, string>> GetOllamaModelList()
        {
            // List (Sorted for first day is Sunday)
            var list = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("llama3.2:3b-instruct-q8_0", "0"),
                new KeyValuePair<string, string>("model 1", "1"),
                new KeyValuePair<string, string>("model 2", "2"),
                new KeyValuePair<string, string>("model 3", "3"),
                new KeyValuePair<string, string>("model 4", "4"),
                new KeyValuePair<string, string>("model 5", "5"),
                new KeyValuePair<string, string>("model 6", "6"),
            };
            return list;
        }

        public void UpdateSettings(PowerLauncherPluginSettings settings)
        {
            if (settings != null && settings.AdditionalOptions != null)
            {
                endpoint = settings.AdditionalOptions.FirstOrDefault(x => x.Key == "LLMEndpoint")?.TextValue ?? "http://localhost:11434/api/generate"; // TODO: use the LLMEndpoint setting

                int modelInt = (int)settings?.AdditionalOptions.First(x => x.Key == "Model").ComboBoxValue;
                model = GetOllamaModelList()[modelInt].Key;

                clipboardTriggerKeyword = settings.AdditionalOptions.FirstOrDefault(x => x.Key == "ClipboardTriggerKeyword")?.TextValue ?? "<clip>";
                sendTriggerKeyword = settings.AdditionalOptions.FirstOrDefault(x => x.Key == "SendTriggerKeyword")?.TextValue ?? "LL";
            }
        }

        public void Init(PluginInitContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Context.API.ThemeChanged += OnThemeChanged;
            UpdateIconPath(Context.API.GetCurrentTheme());
            modelNames = GetModelListAsync().Result;
        }

        private void OnThemeChanged(Theme currentTheme, Theme newTheme) => UpdateIconPath(newTheme);

        private void UpdateIconPath(Theme theme) => IconPath = theme == Theme.Light || theme == Theme.HighContrastWhite ? Context?.CurrentPluginMetadata.IcoPathLight : Context?.CurrentPluginMetadata.IcoPathDark;

        public List<Result> Query(Query query, bool delayedExecution)
        {
            var input = query.Search;

            // Check if the user wants to use the clipboard contents as part of their search query.
            if (input.Contains(clipboardTriggerKeyword))
            {
                try
                {
                    // Retrieve content from the clipboard and replace the keyword with the actual text.
                    var clipboardContent = System.Windows.Application.Current.Dispatcher.Invoke(() => Clipboard.GetText());
                    input = input.Replace(clipboardTriggerKeyword, clipboardContent);
                }
                catch (Exception ex)
                {
                    return
                    [
                        new()
                        {
                            Title = "Clipboard Error",
                            SubTitle = $"Could not read from clipboard: {ex.Message}",
                            IcoPath = IconPath,
                            Action = e => true,
                        },
                    ];
                }
            }

            var response = "End input with: " + sendTriggerKeyword + "'";
            if (input.EndsWith(sendTriggerKeyword, StringComparison.Ordinal))
            {
                input = input[..^sendTriggerKeyword.Length];
                response = QueryLLMStreamAsync(input).Result;
            }

            return
            [
                new()
                {
                    Title = model,
                    SubTitle = response,
                    IcoPath = IconPath,
                    Action = e =>
                    {
                        Context.API.ShowMsg(model, response);
                        return true;
                    },
                    ContextData = new Dictionary<string, string> { { "copy", response } },
                },
            ];
        }

        private sealed class ModelInfo
        {
            public string Name { get; set; }
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

        public System.Windows.Controls.Control CreateSettingPanel()
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
