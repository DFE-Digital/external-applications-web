export class Dashboardpage{

    static selectors = {
        startNewApplicationBtn: '.govuk-button govuk-!-margin-bottom-9',
        
    }

    static clickStartBtn() {
        cy.get(this.selectors.startNewApplicationBtn).click();
        return this;
    }
    
}

export default Dashboardpage;
