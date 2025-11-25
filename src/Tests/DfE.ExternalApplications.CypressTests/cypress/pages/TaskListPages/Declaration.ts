
export class Declaration{

static selectors = {

        Declaration: 'group-declaration-task-declaration-from-all-chairs-of-trustees',
        TaskStatus: 'task-declaration-from-all-chairs-of-trustees-status',


    }

    static clickDeclaration() 
        {
            cy.getById(this.selectors.Declaration).contains('Declaration from all chairs of trustees').click();
        }

    static clickDeclarationIfNotStarted() {
        if (cy.getById(this.selectors.TaskStatus).contains('Not started')) {
            this.clickDeclaration();
        }
    }

    static verifyTaskStatusIsCompleted() {

        cy.getById(this.selectors.TaskStatus).contains('Completed')

    }  
    


}
    




export default Declaration;




