
export class Risks{

static selectors = {

        Risks: 'group-about-transferring-academies-task-risks',
        TaskStatus: 'task-risks-status',
        taskCompleted:'task-details-of-trusts-status',
        saveandcontinue:'save-task-summary-button',
        Markcompletecheckbox:'IsTaskCompleted',


    }

    static clickRisks() 
        {
            cy.getById(this.selectors.Risks).contains('Risks').click();
        }

    static clickRisksIfNotStarted() {
        if (cy.getById(this.selectors.TaskStatus).contains('Not started')) {
            this.clickRisks();
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
    




export default Risks;




