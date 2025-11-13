
export class Members{

static selectors = {

        Members: 'group-about-the-trust-that-academies-are-joining-task-members',
        TaskStatus: 'task-members-status',


    }

    static clickMembers() 
        {
            cy.getById(this.selectors.Members).contains('Members').click();
        }

    static clickMembersIfNotStarted() {
        if (cy.getById(this.selectors.TaskStatus).contains('Not started')) {
            this.clickMembers();
        }
    }

    static verifyTaskStatusIsCompleted() {

        cy.getById(this.selectors.TaskStatus).contains('Completed')

    }  
    


}
    




export default Members;




