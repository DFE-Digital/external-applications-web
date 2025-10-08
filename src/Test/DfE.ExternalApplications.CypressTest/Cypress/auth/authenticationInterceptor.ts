

export class AuthenticationInterceptor {
    register() {
        cy.intercept(
            {
                url: Cypress.env('url') + "/**",
                middleware: true,
            },
            (req) => {
                // Set an auth header on every request made by the browser
                req.headers = {
                    ...req.headers,
                    Authorization: `Bearer ${Cypress.env('authKey')}`,
                    "x-user-context-name": (Cypress.env('username')), // must be present, but not used
                    "x-user-context-id": "", // must be present for antiforgery claims
                    "x-user-ad-id": "",
                    "x-cypress-user": "cypressUser",
                };
            },
        ).as("AuthInterceptor");
    }
}