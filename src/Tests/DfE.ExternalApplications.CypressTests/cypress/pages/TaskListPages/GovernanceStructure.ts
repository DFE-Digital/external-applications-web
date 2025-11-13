
export class GovernanceStructure{

static selectors = {

        GovernanceStructure: 'group-about-the-trust-that-academies-are-joining-task-governance-structure',
        TaskStatus: 'task-governance-structure-status',


    }

    static clickGovernanceStructure() 
        {
            cy.getById(this.selectors.GovernanceStructure).contains('Governance Structure').click();
        }

    static clickGovernanceStructureIfNotStarted() {
        if (cy.getById(this.selectors.TaskStatus).contains('Not started')) {
            this.clickGovernanceStructure();
        }
    }

    static verifyTaskStatusIsCompleted() {

        cy.getById(this.selectors.TaskStatus).contains('Completed')

    }  
    


}
    




export default GovernanceStructure;




