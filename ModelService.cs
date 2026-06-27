using System.Collections.Generic;

public record ChatMessage(string Role, string Content);

public class ModelService
{
    public string ModelName { get; set; }
    public string SystemPrompt { get; set; }
    public List<ChatMessage> SessionMemory { get; } = new();
    public string UserPrompt { get; private set; } = string.Empty;
    public string? Response { get; private set; }

    public ModelService(string modelName, string systemPrompt)
    {
        ModelName = modelName;
        SystemPrompt = systemPrompt;
    }

    public void SetUserPrompt(string prompt)
    {
        UserPrompt = string.IsNullOrWhiteSpace(prompt) ? "Hello" : prompt.Trim();
    }

    public void AddSessionMemory(string role, string content)
    {
        SessionMemory.Add(new ChatMessage(role, content));
    }

    public void SetResponse(string response)
    {
        Response = response;
    }

    public object BuildRequestBody(int numPredict = 10, double temperature = 0.5, int topK = 20, double topP = 0.5, double repeatPenalty = 1.0)
    {
        var messages = new List<object>
        {
            new { role = "system", content = SystemPrompt }
        };

        // Add session memory (previous conversation history) first
        if (SessionMemory.Count > 0)
        {
            foreach (var memory in SessionMemory)
            {
                messages.Add(new { role = memory.Role, content = memory.Content });
            }
        }

        // Add current user prompt last
        messages.Add(new { role = "user", content = UserPrompt });

        return new
        {
            model = ModelName,
            messages,
            stream = false,
            options = new
            {
                num_predict = numPredict,
                temperature,
                top_k = topK,
                top_p = topP,
                repeat_penalty = repeatPenalty
            }
        };
    }
}