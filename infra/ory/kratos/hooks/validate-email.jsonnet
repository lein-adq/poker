// Shapes the web_hook body sent to POST /internal/iam/validate-email so the API only
// has to look at the submitted email, not the whole Kratos flow context.
function(ctx) {
  email: ctx.identity.traits.email,
}
