
export class Contributors{

    static selectors = {
        addContributor: 'add-a-contributor',
        proceedBtn: 'proceed-to-application-form',
        contributor1:'contributor-1',
        contributor2:'contributor-2',
        contributoremail: 'EmailAddress',
        contributorname:'Name',
        sendinvite:'send-email-invite',
        cancel:'cancel',
        emailText: 'test@test.com',
        inviteContributor:'invite-contributors'   
    }

    
static addContributor() {
    cy.getById(this.selectors.addContributor).contains('Add a contributor').click();
    cy.getById(this.selectors.contributoremail).type('ContributorTestuser@gov.uk');
    cy.getById(this.selectors.contributorname).type('Test User');
    cy.getById(this.selectors.cancel).contains('Cancel');
    cy.getById(this.selectors.sendinvite).click();
    return this;
}
static verifyContributor(){
    // Verify if the contributor is added by checking the username
    cy.getById(this.selectors.contributor1).contains(Cypress.env('username'));
}

static verifyNewContributor() {

    // Verify if the new contributor is added by checking the email
    cy.getById(this.selectors.contributor2).contains(this.selectors.emailText);
    // Verify if the new contributor is added by checking the username
    cy.getById(this.selectors.contributor2).contains(this.selectors.contributorname);
    cy.getById(this.selectors.contributor2).contains(this.selectors.cancel);
}

static ClickProceedBtn() {
    cy.getById(this.selectors.proceedBtn).contains('Proceed to the application form').click();
    return this;
}
// This method is used to invite contributors from Task List Page
static inviteContributors() {
    cy.getById(this.selectors.inviteContributor).contains('Invite contributors').click();
}

}
export default Contributors;
