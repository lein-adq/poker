// Shapes the web_hook body sent to POST /internal/iam/on-registered, which upserts the
// user row and grants the 300-chip signup bonus.
function(ctx) {
  userId: ctx.identity.id,
  email: ctx.identity.traits.email,
  timeZoneId: if "timezone" in ctx.identity.traits then ctx.identity.traits.timezone else "UTC",
}
