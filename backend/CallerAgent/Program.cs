using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

Console.WriteLine("CallerAgent starting...");

var http = new HttpClient();
var correctorBase = "http://localhost:5091";
var responderBase = "http://localhost:5092";

Console.Write("Enter your question or statement: ");
var userInput = Console.ReadLine() ?? "teh capital of france?";

var corrReq = new { capability = "correct_text", input = new { text = userInput } };
var corrContent = new StringContent(JsonSerializer.Serialize(corrReq), Encoding.UTF8, "application/json");
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "demo-token");
var corrResp = await http.PostAsync($"{correctorBase}/invoke", corrContent);
corrResp.EnsureSuccessStatusCode();
var corrJson = await corrResp.Content.ReadFromJsonAsync<JsonElement>();
var corrected = corrJson.GetProperty("output").GetProperty("corrected").GetString() ?? userInput;
Console.WriteLine($"\nCorrected Text: {corrected}");

var respReq = new { capability = "answer", input = new { text = corrected }, context = new { original = userInput } };
var respContent = new StringContent(JsonSerializer.Serialize(respReq), Encoding.UTF8, "application/json");
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "demo-token");
var resp = await http.PostAsync($"{responderBase}/invoke", respContent);
resp.EnsureSuccessStatusCode();
var respJson = await resp.Content.ReadFromJsonAsync<JsonElement>();
var answer = respJson.GetProperty("output").GetProperty("answer").GetString() ?? "(no answer)";
Console.WriteLine($"\nFinal Answer:\n{answer}\n");
