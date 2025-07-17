
export class GovUKPage{

    static selectors = {
        startButton: 'a[href="/dashboard"][role="button"]',
        
    }

      static getHomePage() {
        cy.visit(`https://s184d01-rsd-frontdoor-extapp-web-fvd9cqh0fhbegsbr.a03.azurefd.net/`);
    }
static scrollToStartButton() {
        cy.get(this.selectors.startButton).scrollIntoView();
    }

    static clickStartBtn() {
        cy.get(this.selectors.startButton).click();
        return this;
    }

    
}
export default GovUKPage;
