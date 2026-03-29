// ═══════════════════════════════════════════════════════════════════════════════
// FIXED refresh token code — replace the block after deserializing EntraTokenResponse
//
// THE BUG:
//
//   var user = await _db?.Users
//       ?.FirstOrDefaultAsync(x => x.Id == userId);
//
//   If _db is null, the ?. short-circuits and the expression becomes:
//       await (Task<User?>)null  →  NullReferenceException
//
//   You cannot await null. The ?. operator returns null (not a Task),
//   and await expects a Task.
//
//   Same problem with:
//       tr.AccessToken = _tokenService?.GenerateJwtToken(user);
//   If _tokenService is null, you silently set AccessToken to null.
//
// THE FIX:
//   Guard with an if-statement instead of ?. on the await chain.
// ═══════════════════════════════════════════════════════════════════════════════

// ─── REPLACE THIS ────────────────────────────────────────────────────────────
//
//  if (token != null &&
//      !string.IsNullOrWhiteSpace(token.UserId) &&
//      Guid.TryParse(token.UserId, out var userId))
//  {
//      var user = await _db?.Users
//          ?.FirstOrDefaultAsync(x => x.Id == userId);
//
//      if (user != null && tr != null)
//      {
//          tr.AccessToken = _tokenService?.GenerateJwtToken(user);
//      }
//  }
//
// ─── WITH THIS ───────────────────────────────────────────────────────────────

if (tr != null &&
    token != null &&
    !string.IsNullOrWhiteSpace(token.UserId) &&
    Guid.TryParse(token.UserId, out var userId) &&
    _db != null &&
    _tokenService != null)
{
    var user = await _db.Users
        .FirstOrDefaultAsync(x => x.Id == userId);

    if (user != null)
    {
        tr.AccessToken = _tokenService.GenerateJwtToken(user);
    }
}
