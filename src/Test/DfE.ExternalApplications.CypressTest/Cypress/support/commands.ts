
import "cypress-axe";
import { Logger } from "../Common/logger";
import { RuleObject } from "axe-core";
import { AuthenticationInterceptor } from "../auth/authenticationInterceptor";


Cypress.Commands.add("getByTestId", (id) => {
    cy.get(`[data-testid="${id}"]`);
});

Cypress.Commands.add("containsByTestId", (id) => {
    cy.get(`[data-testid*="${id}"]`);
});

Cypress.Commands.add("getById", (id) => {
    cy.get(`[id="${id}"]`);
});

Cypress.Commands.add("getByClass", (className) => {
    cy.get(`[class="${className}"]`);
});

Cypress.Commands.add("getByName", (name) => {
    cy.get(`[name="${name}"]`);
});

Cypress.Commands.add("getByRole", (role) => {
    cy.get(`[role="${role}"]`);
});

Cypress.Commands.add("getByLabelFor", (labelFor) => {
    cy.get(`[for="${labelFor}"]`);
})

Cypress.Commands.add("getByRadioOption", (radioText: string) => {
    cy.contains(radioText)
        .invoke('attr', 'for')
        .then((id) => {
            cy.get('#' + id);
        });
})

Cypress.Commands.add("assertChildList", (selector: string, values: string[]) => {
    cy.getByTestId(selector)
        .children()
        .should("have.length", values.length)
        .each((el, i) => {
            expect(el.text()).to.equal(values[i]);
        });
});



Cypress.Commands.add('typeFast', { prevSubject: 'element' }, (subject: JQuery<HTMLElement>, text: string) => {
    cy.wrap(subject).invoke('val', text);
  });

Cypress.Commands.add("enterDate", (idPrefix: string, day: string, month: string, year: string) => {

    if (day.length > 0) {
        cy.getById(`${idPrefix}-day`).typeFast(day);
    }

    if (month.length > 0) {
        cy.getById(`${idPrefix}-month`).typeFast(month);
    }

    if (year.length > 0) {
        cy.getById(`${idPrefix}-year`).typeFast(year);
    }
});

Cypress.Commands.add("checkDate", (idPrefix: string, day: string, month: string, year: string) => {
    cy.getById(`${idPrefix}-day`).should("have.value", day);
    cy.getById(`${idPrefix}-month`).should("have.value", month);
    cy.getById(`${idPrefix}-year`).should("have.value", year);
});

Cypress.Commands.add("hasAddress", (id: string, line1: string, line2: string, line3: string) => {
    if (line1 === "Empty") {
        cy.getByTestId(id).should("contain.text", "Empty");

        return;
    }

    cy.getByTestId(id).find("[data-testid='address-line1']").should("contain.text", line1);
    cy.getByTestId(id).find("[data-testid='address-line2']").should("contain.text", line2);
    cy.getByTestId(id).find("[data-testid='address-line3']").should("contain.text", line3);
});



// Login command to clear cookies and local storage, then visit the application URL
//Cypress.Commands.add("login", () => {
//    cy.clearCookies();
//    cy.clearLocalStorage();

    // // Intercept all browser requests and add our special auth header
    // // Means we don't have to use azure to authenticate
//    new AuthenticationInterceptor().register();
//});
// Login command to clear cookies and local storage, then visit the application URL
Cypress.Commands.add("login", () => {
    cy.clearCookies();
    cy.clearLocalStorage();
    cy.visit(Cypress.env('url'));
});


// Custom command to Save and Continue
Cypress.Commands.add("SaveAndContinue", () => {
    cy.getById('save-and-continue-button').click();
});
//Custom command to Mark the Section as complete
 Cypress.Commands.add ("clickMarkCompleteCheckbox", () => {
        cy.getById('IsTaskCompleted').check();
        cy.getById('IsTaskCompleted').should('be.checked');
    });
// Custom command to Save Task Summary
            Cypress.Commands.add("SaveTaskSummary", () => {
    cy.getById('save-task-summary-button').click();
            });
// Custom command to execute accessibility tests using Axe
Cypress.Commands.add("executeAccessibilityTests", () => {
    Logger.log("Executing the command");
    const wcagStandards = ["wcag22aa"];
    const impactLevel = ["critical", "minor", "moderate", "serious"];
    const continueOnFail = false;

    // Ensure that the axe dependency is available in the browser
    Logger.log("Inject Axe");
    cy.injectAxe();

    Logger.log("Checking accessibility");
    cy.checkA11y(
        null,
        {
            runOnly: {
                type: "tag",
                values: wcagStandards,
            },
            includedImpacts: impactLevel,
        },
        null,
        continueOnFail
    );
});