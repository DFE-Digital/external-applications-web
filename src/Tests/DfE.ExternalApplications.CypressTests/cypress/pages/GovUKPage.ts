
export class GovUKPage{

    static selectors = {
        startButton: 'start-application-button',
        
    }

      static getHomePage() {
       cy.visit(Cypress.env('url'));;
    }
static scrollToStartButton() {
        cy.getById(this.selectors.startButton).scrollIntoView();
    }

    static clickStartBtn() {
        cy.getById(this.selectors.startButton).click();
        return this;
    }

    
}
export default GovUKPage;
