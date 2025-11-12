
export class Trustees{

static selectors = {

        Trustees: 'group-about-the-trust-that-academies-are-joining-task-trustees',
        TaskStatus: 'task-trustees-status',


    }

    static clickTrustees() 
        {
            cy.getById(this.selectors.Trustees).contains('Trustees').click();
        }

    static clickTrusteesIfNotStarted() {
        if (cy.getById(this.selectors.TaskStatus).contains('Not started')) {
            this.clickTrustees();
        }
    }

    static verifyTaskStatusIsCompleted() {

        cy.getById(this.selectors.TaskStatus).contains('Completed')

    }  
    


}
    




export default Trustees;




