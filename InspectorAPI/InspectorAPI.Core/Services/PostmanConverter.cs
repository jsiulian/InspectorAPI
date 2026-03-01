using System.Text.Json;
using System.Text.Json.Nodes;
using InspectorAPI.Core.Models;

namespace InspectorAPI.Core.Services;

public static class PostmanConverter
{
    public static Collection FromPostman(string json)
    {
        var root = JsonNode.Parse(json) ?? throw new InvalidOperationException("Invalid JSON");

        var col = new Collection
        {
            Name = root["info"]?["name"]?.GetValue<string>() ?? "Imported Collection"
        };

        if (root["item"] is JsonArray items)
            ParseItems(items, col.Requests, col.Folders);

        return col;
    }

    private static void ParseItems(JsonArray items, List<SavedRequest> requests, List<CollectionFolder> folders)
    {
        foreach (var item in items)
        {
            if (item is null) continue;

            if (item["item"] is JsonArray subItems)
            {
                var folder = new CollectionFolder { Name = item["name"]?.GetValue<string>() ?? "Folder" };
                ParseItems(subItems, folder.Requests, folder.Folders);
                folders.Add(folder);
            }
            else if (item["request"] is not null)
            {
                requests.Add(ParseRequest(item));
            }
        }
    }

    private static SavedRequest ParseRequest(JsonNode item)
    {
        var req = item["request"]!;
        var method = req["method"]?.GetValue<string>() ?? "GET";

        var urlNode = req["url"];
        var rawUrl = urlNode is JsonValue
            ? urlNode.GetValue<string>()
            : (urlNode?["raw"]?.GetValue<string>() ?? string.Empty);

        var headers = new List<HeaderItem>();
        if (req["header"] is JsonArray headerArr)
        {
            foreach (var h in headerArr)
            {
                if (h is null) continue;
                headers.Add(new HeaderItem
                {
                    Key = h["key"]?.GetValue<string>() ?? string.Empty,
                    Value = h["value"]?.GetValue<string>() ?? string.Empty,
                    IsEnabled = !(h["disabled"]?.GetValue<bool>() ?? false)
                });
            }
        }

        var queryParams = new List<HeaderItem>();
        if (urlNode is JsonObject urlObj && urlObj["query"] is JsonArray queryArr)
        {
            foreach (var q in queryArr)
            {
                if (q is null) continue;
                queryParams.Add(new HeaderItem
                {
                    Key = q["key"]?.GetValue<string>() ?? string.Empty,
                    Value = q["value"]?.GetValue<string>() ?? string.Empty,
                    IsEnabled = !(q["disabled"]?.GetValue<bool>() ?? false)
                });
            }
        }

        var body = string.Empty;
        var contentType = "application/json";
        if (req["body"] is JsonObject bodyObj)
        {
            body = bodyObj["raw"]?.GetValue<string>() ?? string.Empty;
            var lang = bodyObj["options"]?["raw"]?["language"]?.GetValue<string>() ?? "json";
            contentType = lang switch
            {
                "xml" => "application/xml",
                "html" => "text/html",
                "text" => "text/plain",
                _ => "application/json"
            };
        }

        return new SavedRequest
        {
            Name = item["name"]?.GetValue<string>() ?? "Request",
            Request = new HttpRequestModel
            {
                Url = rawUrl,
                Method = method,
                Headers = headers,
                QueryParams = queryParams,
                Body = body,
                BodyContentType = contentType
            }
        };
    }

    public static string ToPostman(Collection col)
    {
        var root = new JsonObject
        {
            ["info"] = new JsonObject
            {
                ["_postman_id"] = col.Id.ToString(),
                ["name"] = col.Name,
                ["schema"] = "https://schema.getpostman.com/json/collection/v2.1.0/collection.json"
            },
            ["item"] = BuildItems(col.Folders, col.Requests)
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonArray BuildItems(List<CollectionFolder> folders, List<SavedRequest> requests)
    {
        var arr = new JsonArray();
        foreach (var folder in folders)
            arr.Add(new JsonObject
            {
                ["name"] = folder.Name,
                ["item"] = BuildItems(folder.Folders, folder.Requests)
            });
        foreach (var req in requests)
            arr.Add(BuildRequestItem(req));
        return arr;
    }

    private static JsonObject BuildRequestItem(SavedRequest req)
    {
        var headers = new JsonArray();
        foreach (var h in req.Request.Headers)
            headers.Add(new JsonObject { ["key"] = h.Key, ["value"] = h.Value, ["disabled"] = !h.IsEnabled });

        var query = new JsonArray();
        foreach (var q in req.Request.QueryParams)
            query.Add(new JsonObject { ["key"] = q.Key, ["value"] = q.Value, ["disabled"] = !q.IsEnabled });

        var urlObj = new JsonObject { ["raw"] = req.Request.Url };
        if (query.Count > 0) urlObj["query"] = query;

        var requestObj = new JsonObject
        {
            ["method"] = req.Request.Method,
            ["header"] = headers,
            ["url"] = urlObj
        };

        if (!string.IsNullOrWhiteSpace(req.Request.Body))
        {
            var lang = req.Request.BodyContentType switch
            {
                "application/xml" => "xml",
                "text/html" => "html",
                "text/plain" => "text",
                _ => "json"
            };
            requestObj["body"] = new JsonObject
            {
                ["mode"] = "raw",
                ["raw"] = req.Request.Body,
                ["options"] = new JsonObject { ["raw"] = new JsonObject { ["language"] = lang } }
            };
        }

        return new JsonObject
        {
            ["name"] = req.Name,
            ["request"] = requestObj,
            ["response"] = new JsonArray()
        };
    }
}
