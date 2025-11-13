
export class OutgoingTrust{

static selectors = {

        DetailsofTrusts: 'group-about-the-trusts-that-academies-are-leaving-task-details-of-trusts',
        TaskStatus: 'task-details-of-trusts-status',
        taskCompleted:'task-details-of-trusts-status',
        saveandcontinue:'save-task-summary-button',
        Markcompletecheckbox:'IsTaskCompleted',


    }

    static clickDetailsofTrust() 
        {
            cy.getById(this.selectors.DetailsofTrusts).contains('Details of trusts').click();
        }

    static clickDetailsofTrustIfNotStarted() {
        if (cy.getById(this.selectors.TaskStatus).contains('Not started')) {
            this.clickDetailsofTrust();
        }
    }

    static verifyTaskStatusIsCompleted() {

        cy.getById(this.selectors.TaskStatus).contains('Completed')

    }  
    
    static clickMarkCompleteCheckbox() {
        cy.getById(this.selectors.Markcompletecheckbox).check();
        cy.getById(this.selectors.Markcompletecheckbox).should('be.checked');
    }
    
    static clickSaveAndContinue() {
        cy.getById(this.selectors.saveandcontinue).contains('Save and continue').click();
        return this;
    }



}
    




export default OutgoingTrust;




