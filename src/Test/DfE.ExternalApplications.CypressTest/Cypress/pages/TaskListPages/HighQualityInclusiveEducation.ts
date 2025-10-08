
export class HighQualityInclusiveEducation{

static selectors = {

        HighQualityInclusiveEducation: 'group-about-the-trust-that-academies-are-joining-task-high-quality-and-inclusive-education',
        TaskStatus: 'task-high-quality-and-inclusive-education-status',


    }

    static clickDetailsofTrust() 
        {
            cy.getById(this.selectors.HighQualityInclusiveEducation).contains('High-quality and inclusive education').click();
        }

    static clickHighQualityInclusiveEducationIfNotStarted() {
        if (cy.getById(this.selectors.TaskStatus).contains('Not started')) {
            this.clickDetailsofTrust();
        }
    }

    static verifyTaskStatusIsCompleted() {

        cy.getById(this.selectors.TaskStatus).contains('Completed')

    }  
    


}
    

export default HighQualityInclusiveEducation;




