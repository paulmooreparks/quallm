﻿using System.Net.Http.Headers;
using System.Text;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using AITinker.Core.Services;
using AITinker.Core.Models;
using AITinker.OpenAI.Models;

namespace AITinker.OpenAI.Services;

public class OpenAIService : ILLMService {
    private readonly IWriteableSection<OpenAISettings> _config;
    private readonly OpenAIDefaults _defaults;
    private readonly OpenAIOptions _options;
    private readonly HttpClient _httpClient;
    private List<Tuple<string, string>> _history;

    public OpenAIService(IWriteableSection<OpenAISettings> config, OpenAIDefaults defaults, OpenAIOptions options) {
        _config = config;
        _defaults = defaults;
        _options = options;
        _history = new List<Tuple<string, string>>();
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
    }

    public string ApiKey { 
        get {
            return _config?.Value.ApiKey ?? string.Empty;
        } 
        set {
            _config.Value.ApiKey = value;
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config.Value.ApiKey);
        }
    }

    public string ApiUrl {
        get {
            return _config.Value?.ApiUrl ?? _defaults.ApiUrl ?? "https://api.openai.com/v1/chat/completions";
        }
        set {
            _config.Value.ApiUrl = value;
        }
    }

    public string Model {
        get {
            return _config.Value?.Model ?? _defaults.Model ?? "gpt-4o-mini";
        }
        set {
            _config.Value.Model = value;
        }
    }

    public string SystemContent {
        get {
            return _config.Value?.SystemContent ?? _defaults.SystemContent ?? "You are a helpful assistant.";
        }
        set {
            _config.Value.SystemContent = value;
        }
    }

    public double Temperature {
        get {
            return _config.Value?.Temperature ?? _defaults.Temperature ?? 0.5;
        }
        set {
            _config.Value.Temperature = value;
        }
    }

    public OpenAIOptions Options {
        get => _options;
    }

    public void SaveSettings() {
        _config.Update(config => { });
    }

    public async Task<LLMResponse> SendMessage(string message, string extra) {
        var messageList = new List<object>();

        if (!string.IsNullOrEmpty(SystemContent)) {
            var systemMessage = new {
                role = "system",
                content = SystemContent
            };

            messageList.Add(systemMessage);
        }

        foreach (var item in _history) {
            var historyPrompt = new {
                role = "user",
                content = item.Item1
            };

            messageList.Add(historyPrompt);

            var historyResponse = new {
                role = "assistant",
                content = item.Item2
            };

            messageList.Add(historyResponse);
        }

        var prompt = new {
            role = "user",
            content = message
        };

        messageList.Add(prompt);

        if (!string.IsNullOrEmpty(extra)) {
            var extraMessage = new {
                role = "user",
                content = extra
            };

            messageList.Add(extraMessage);
        }

        var requestBody = new {
            model = Model,
            messages = messageList.ToArray(),
            temperature = Temperature
        };

        var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(ApiUrl, content);

        if (response.IsSuccessStatusCode) {
            var llmResponse = new LLMResponse();
            var jsonResponse = JObject.Parse(await response.Content.ReadAsStringAsync());

            var choices = jsonResponse["choices"];

            if (choices is not null) {
                llmResponse.Content = choices[0]?["message"]?["content"]?.ToString() ?? string.Empty;
                _history.Add(Tuple.Create<string, string>(message, llmResponse.Content));

                if (choices.Count() > 1) {
                    for (var i = 1; i < choices.Count(); i++) {
                        llmResponse.ExtraData[$"choice_{i}"] = choices[i]?["message"]?["content"]?.ToString() ?? string.Empty;
                    }
                }
            }

            if (jsonResponse.ContainsKey("usage")) {
                var usage = jsonResponse["usage"];

                if (usage is not null) {
                    llmResponse.ExtraData["prompt_tokens"] = usage["prompt_tokens"]?.ToString() ?? string.Empty;
                    llmResponse.ExtraData["completion_tokens"] = usage["completion_tokens"]?.ToString() ?? string.Empty;
                    llmResponse.ExtraData["total_tokens"] = usage["total_tokens"]?.ToString() ?? string.Empty;
                }
            }

            return llmResponse;
        }
        else {
            throw new Exception($"Error: {response.StatusCode}, Response:{Environment.NewLine}{await response.Content.ReadAsStringAsync()}");
        }
    }

    public void UpdateFromSettings(ITinkerSettings settings) {
        if (settings is not null and OpenAISettings openAISettings) {
            ApiKey = openAISettings?.ApiKey ?? string.Empty;
            ApiUrl = openAISettings?.ApiUrl ?? string.Empty;
            Model = openAISettings?.Model ?? string.Empty;
            SystemContent = openAISettings?.SystemContent ?? string.Empty;
            Temperature = openAISettings?.Temperature ?? default;
        }
    }
}
