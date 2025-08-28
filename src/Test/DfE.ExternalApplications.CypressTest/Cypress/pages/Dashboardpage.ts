export class Dashboardpage{

    static selectors = {
        startNewApplicationBtn: 'start-new-application-button',
        
    }

    static clickStartBtn() {
        cy.getById(this.selectors.startNewApplicationBtn).click();
        return this;
    }
    
}

export default Dashboardpage;
