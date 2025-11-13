
export class HighQualityInclusiveEducation{

static selectors = {

        Leadership: 'group-about-the-trust-that-academies-are-joining-task-leadership-and-work-force',
        TaskStatus: 'task-leadership-and-work-force-status',


    }

    static clickLeadership() 
        {
            cy.getById(this.selectors.Leadership).contains('Leadership and work force').click();
        }

    static clickLeadershipIfNotStarted() {
        if (cy.getById(this.selectors.TaskStatus).contains('Not started')) {
            this.clickLeadership();
        }
    }

    static verifyTaskStatusIsCompleted() {

        cy.getById(this.selectors.TaskStatus).contains('Completed')

    }  
    


}
    




export default HighQualityInclusiveEducation;




