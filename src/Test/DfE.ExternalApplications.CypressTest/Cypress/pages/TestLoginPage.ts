
export class TestLoginPage{

    static selectors = {
        email: 'Input.Email',
        continuebtn: 'test-login-button',
        
    }

    
 static enterUsername(username: string) {
        cy.getById(this.selectors.email).click().type(username);
        return this;
    
}

static ContinueBtn() {
    cy.getById(this.selectors.continuebtn).contains('Continue').click();
    return this;


}

}
export default TestLoginPage;
