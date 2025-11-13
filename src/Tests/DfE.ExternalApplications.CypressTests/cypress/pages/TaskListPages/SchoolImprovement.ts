
export class SchoolImprovement{

static selectors = {

        SchoolImprovement: 'group-about-the-trust-that-academies-are-joining-task-school-improvement',
        TaskStatus: 'task-school-improvement-status',


    }

    static clickSchoolImprovement() 
        {
            cy.getById(this.selectors.SchoolImprovement).contains('School improvement').click();
        }

    static clickSchoolImprovementIfNotStarted() {
        if (cy.getById(this.selectors.TaskStatus).contains('Not started')) {
            this.clickSchoolImprovement();
        }
    }

    static verifyTaskStatusIsCompleted() {

        cy.getById(this.selectors.TaskStatus).contains('Completed')

    }  
    


}
    
export default SchoolImprovement;




