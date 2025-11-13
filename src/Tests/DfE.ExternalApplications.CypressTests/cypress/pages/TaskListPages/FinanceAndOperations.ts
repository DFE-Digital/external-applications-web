
export class FinanceAndOperations{

static selectors = {

        FinanceAndOperations: 'group-about-the-trust-that-academies-are-joining-task-finance-and-operations',
        TaskStatus: 'task-finance-and-operations-status',


    }

    static clickFinanceAndOperations() 
        {
            cy.getById(this.selectors.FinanceAndOperations).contains('Finance and operations').click();
        }
E
    static clickFinanceAndOperationsIfNotStarted() {
        if (cy.getById(this.selectors.TaskStatus).contains('Not started')) {
            this.clickFinanceAndOperations();
        }
    }

    static verifyTaskStatusIsCompleted() {

        cy.getById(this.selectors.TaskStatus).contains('Completed')

    }  
    


}
    




export default FinanceAndOperations;




