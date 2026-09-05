using Microsoft.AspNetCore.Mvc.Filters;

namespace eCommerce.Inventory.Api.Filters;

/// <summary>
/// Abilita il buffering del corpo della richiesta prima del model binding. Chiamare
/// Request.EnableBuffering() dentro l'azione arriva troppo tardi: [FromBody] ha già
/// consumato lo stream non-seekable di Kestrel, quindi una rilettura successiva del
/// corpo (es. per verificare una firma HMAC) otterrebbe una stringa vuota.
/// </summary>
public class EnableRequestBodyBufferingAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        context.HttpContext.Request.EnableBuffering();
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
