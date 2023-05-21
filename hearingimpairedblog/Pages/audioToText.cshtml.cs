using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace hearingimpairedblog.Pages
{
    public class audioToTextModel : PageModel
    {
        private static readonly HttpClient HttpClient = new HttpClient();
        static string API_Key = "7f64872363bb45b3bba69705e18d9e9e";
        public string Transcript { get; set; }

        public audioToTextModel() 
        {
            Transcript = "";
        }   

        public void OnPost(IFormFile file)
        {
            HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(API_Key);

            var transcribeFileProcess = async () =>
            {
                // Get file path
                var filePath = Path.GetTempFileName();
                string newPath = System.IO.Path.Combine(Path.GetDirectoryName(filePath), file.FileName);
                using (var stream = System.IO.File.Create(newPath))
                {
                    await file.CopyToAsync(stream);
                }

                // Upload File
                var uploadedFileUrl = await UploadFileAsync(newPath);

                // Transcribe File
                return await GetTransciptAsync(uploadedFileUrl);       
            };

            Transcript = transcribeFileProcess.Invoke().Result;
        }

        private static async Task<string> UploadFileAsync(string path)
        {
            await using var fileStream = System.IO.File.OpenRead(path);
            using var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            using var response = await HttpClient.PostAsync("https://api.assemblyai.com/v2/upload", fileContent);

            if (response.IsSuccessStatusCode == false)
            {
                throw new Exception($"Error: {response.StatusCode} - {response.ReasonPhrase}");

            }

            var jsonDoc = await response.Content.ReadFromJsonAsync<JsonDocument>();
            return jsonDoc.RootElement.GetProperty("upload_url").GetString();

        }

        private async Task<string> GetTransciptAsync(string audioUrl)
        {
            var data = new { audio_url = audioUrl };
            var content = new StringContent(System.Text.Json.JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");

            using var response = await HttpClient.PostAsync("https://api.assemblyai.com/v2/transcript", content);
            var responseJson = await response.Content.ReadFromJsonAsync<JsonDocument>();

            var trascriptId = responseJson.RootElement.GetProperty("id").GetString();
            var pollingEndpoint = $"https://api.assemblyai.com/v2/transcript/{trascriptId}";

            while (true)
            {
                var pollingResponse = await HttpClient.GetAsync(pollingEndpoint);
                var pollingJsonDocument = await pollingResponse.Content.ReadFromJsonAsync<JsonDocument>();
                var pollingJsonObject = pollingJsonDocument.RootElement;

                var status = pollingJsonObject.GetProperty("status").GetString();
                switch (status)
                {
                    case "processing":
                        await Task.Delay(TimeSpan.FromSeconds(3));
                        break;
                    case "completed":
                        return pollingJsonObject.GetProperty("text").GetString();
                    case "error":
                        var error = pollingJsonObject.GetProperty("error").GetString();
                        throw new Exception($"Transcription failed: {error}");
                    default:
                        throw new Exception("This code should not be reachable.");
                }
            }
        }

    }

}
