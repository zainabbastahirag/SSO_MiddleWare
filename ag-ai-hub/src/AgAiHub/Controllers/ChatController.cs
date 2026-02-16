using AgAiHub.Middleware;
using AgAiHub.Models;
using AgAiHub.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgAiHub.Controllers;

[ApiController]
[Route("api/ai")]
public class ChatController : ControllerBase
{
    private readonly AiService _ai;
    private readonly ILogger<ChatController> _logger;

    public ChatController(AiService ai, ILogger<ChatController> logger)
    {
        _ai = ai;
        _logger = logger;
    }

    /// <summary>
    /// Chat completion. Send messages, get an AI response.
    /// Set "stream": true to get Server-Sent Events.
    /// </summary>
    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request)
    {
        var productCode = HttpContext.GetProductCode();

        if (request.Messages.Count == 0)
            return BadRequest(new AiErrorResponse { Error = "Messages cannot be empty", Code = "empty_messages" });

        try
        {
            if (request.Stream)
            {
                return await StreamChat(productCode, request);
            }

            var response = await _ai.ChatAsync(productCode, request, HttpContext.RequestAborted);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chat failed for product {Product}", productCode);
            return StatusCode(500, new AiErrorResponse
            {
                Error = "AI request failed",
                Code = "ai_error",
                Details = ex.Message
            });
        }
    }

    /// <summary>
    /// Generate embeddings for the given text inputs.
    /// </summary>
    [HttpPost("embeddings")]
    public async Task<IActionResult> Embeddings([FromBody] EmbeddingRequest request)
    {
        var productCode = HttpContext.GetProductCode();

        if (request.Inputs.Count == 0)
            return BadRequest(new AiErrorResponse { Error = "Inputs cannot be empty", Code = "empty_inputs" });

        try
        {
            var response = await _ai.EmbedAsync(productCode, request, HttpContext.RequestAborted);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Embedding failed for product {Product}", productCode);
            return StatusCode(500, new AiErrorResponse
            {
                Error = "Embedding request failed",
                Code = "ai_error",
                Details = ex.Message
            });
        }
    }

    /// <summary>
    /// Document operations: summarize, extract, Q&A, classify.
    /// </summary>
    [HttpPost("document")]
    public async Task<IActionResult> Document([FromBody] DocumentRequest request)
    {
        var productCode = HttpContext.GetProductCode();

        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest(new AiErrorResponse { Error = "Content cannot be empty", Code = "empty_content" });

        try
        {
            var response = await _ai.ProcessDocumentAsync(productCode, request, HttpContext.RequestAborted);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Document processing failed for product {Product}", productCode);
            return StatusCode(500, new AiErrorResponse
            {
                Error = "Document processing failed",
                Code = "ai_error",
                Details = ex.Message
            });
        }
    }

    private async Task<IActionResult> StreamChat(string productCode, ChatRequest request)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        try
        {
            await foreach (var chunk in _ai.ChatStreamAsync(productCode, request, HttpContext.RequestAborted))
            {
                await Response.WriteAsync($"data: {chunk}\n\n", HttpContext.RequestAborted);
                await Response.Body.FlushAsync(HttpContext.RequestAborted);
            }

            await Response.WriteAsync("data: [DONE]\n\n", HttpContext.RequestAborted);
            await Response.Body.FlushAsync(HttpContext.RequestAborted);
        }
        catch (OperationCanceledException) { }

        return new EmptyResult();
    }
}
